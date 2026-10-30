using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using System.Web.Script.Serialization;

public class Control {
 public string action="deadbug"; public double time=-1; public double yaw=0;
 public bool stream=true; public bool connected=true; public string status="OK";
 public bool motorFail=false; public bool stop=false;
}
public class Bone { public string name; public float[] values; }
public class Frame { public List<Bone> bones=new List<Bone>(); }
public class Simulator {
 static volatile Control control=new Control();
 static string root; static JavaScriptSerializer json=new JavaScriptSerializer();
 static string[] parts={"left_upper_arm","left_lower_arm","right_upper_arm","right_lower_arm","left_upper_leg","left_lower_leg","right_upper_leg","right_lower_leg","chest","waist"};
 static Dictionary<string,float[]> previous=new Dictionary<string,float[]>();
 static void Smooth(string name,float[] v){float[] p;if(!previous.TryGetValue(name,out p))p=new float[]{0,0,0,0,0,0,1};float dot=0;for(int i=3;i<7;i++)dot+=p[i]*v[i];if(dot<0){for(int i=3;i<7;i++)v[i]=-v[i];dot=-dot;}double angle=2*Math.Acos(Math.Min(1,Math.Max(0,dot)))*180/Math.PI;float t=angle>6?(float)(6/angle):1;double norm=0;for(int i=3;i<7;i++){v[i]=p[i]+(v[i]-p[i])*t;norm+=v[i]*v[i];}float scale=(float)Math.Sqrt(norm);for(int i=3;i<7;i++)v[i]/=scale;previous[name]=(float[])v.Clone();}
 static object logLock=new object(); static Stopwatch clock=Stopwatch.StartNew();
 static void Log(string text){lock(logLock) File.AppendAllText(Path.Combine(root,"events.log"),DateTime.UtcNow.ToString("o")+" "+text+Environment.NewLine);}
 static void Text(BinaryWriter w,string text){byte[] bytes=Encoding.UTF8.GetBytes(text);w.Write(bytes);w.Write((byte)0);while(w.BaseStream.Position%4!=0)w.Write((byte)0);}
 static void Number(BinaryWriter w,float x){byte[] b=BitConverter.GetBytes(x);Array.Reverse(b);w.Write(b);}
 static byte[] Message(string address,string name,float[] data){using(var ms=new MemoryStream())using(var w=new BinaryWriter(ms)){Text(w,address);Text(w,",s"+new string('f',data.Length));Text(w,name);foreach(float x in data)Number(w,x);return ms.ToArray();}}
 static byte[] Scalar(string address,bool integer,float value){using(var ms=new MemoryStream())using(var w=new BinaryWriter(ms)){Text(w,address);Text(w,integer?",i":",f");if(integer){byte[] b=BitConverter.GetBytes((int)value);Array.Reverse(b);w.Write(b);}else Number(w,value);return ms.ToArray();}}
 static List<Frame> Load(string action){using(var r=new BinaryReader(File.OpenRead(Path.Combine(root,"data",action+".vmc")))){int count=r.ReadInt32();var frames=new List<Frame>();for(int i=0;i<count;i++){var f=new Frame();int bones=r.ReadInt32();for(int j=0;j<bones;j++){var b=new Bone{name=r.ReadString(),values=new float[7]};for(int k=0;k<7;k++)b.values[k]=r.ReadSingle();f.bones.Add(b);}frames.Add(f);}return frames;}}
 static void Http(){var listener=new TcpListener(IPAddress.Loopback,18761);listener.Start();Log("HTTP listening 127.0.0.1:18761; SIMULATED trackers only");while(!control.stop){if(!listener.Pending()){Thread.Sleep(10);continue;}var client=listener.AcceptTcpClient();ThreadPool.QueueUserWorkItem(delegate{using(client){try{client.ReceiveTimeout=1000;var stream=client.GetStream();var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);string request=reader.ReadLine()??"";string line;while(!string.IsNullOrEmpty(line=reader.ReadLine())){}string path=request.Split(' ')[1];string body="{}";int status=200;Control c=control;if(path.StartsWith("/trackers")){var trackers=new List<object>();foreach(string part in parts)trackers.Add(new{hardwareId="SIM-"+part,bodyPosition="body:"+part,ip="127.0.0.1:18761/"+part,connected=c.connected,status=c.status});body=new JavaScriptSerializer().Serialize(new{apiVersion=1,trackers=trackers});}else if(path.Contains("/motor")){Log("SIMULATED MOTOR "+path);status=c.motorFail?503:200;}else status=404;byte[] payload=Encoding.UTF8.GetBytes(body);byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 "+status+" Result\r\nContent-Type: application/json\r\nContent-Length: "+payload.Length+"\r\nConnection: close\r\n\r\n");stream.Write(header,0,header.Length);stream.Write(payload,0,payload.Length);}catch(Exception e){Log("HTTP "+e.Message);}}});}listener.Stop();}
 public static int Main(string[] args){root=AppDomain.CurrentDomain.BaseDirectory;try{int port=args.Length>0?int.Parse(args[0]):39539;var thread=new Thread(Http){IsBackground=true};thread.Start();var udp=new UdpClient();string loaded="";List<Frame> frames=null;double started=0;DateTime stamp=DateTime.MinValue;Log("SIMULATOR started UDP loopback:"+port);while(!control.stop){string path=Path.Combine(root,"control.json");DateTime next=File.GetLastWriteTimeUtc(path);if(next!=stamp){try{Control parsed=json.Deserialize<Control>(File.ReadAllText(path));control=parsed;stamp=next;}catch(Exception e){Log("CONTROL "+e.Message);}}Control c=control;if(c.action!=loaded){frames=Load(c.action);loaded=c.action;started=clock.Elapsed.TotalSeconds;Log("ACTION "+loaded);}if(c.stream){double seconds=c.time>=0?c.time:clock.Elapsed.TotalSeconds-started;int index=(int)(seconds*30)%frames.Count;byte[] ok=Scalar("/VMC/Ext/OK",true,c.connected?1:0);udp.Send(ok,ok.Length,"127.0.0.1",port);byte[] t=Scalar("/VMC/Ext/T",false,(float)clock.Elapsed.TotalSeconds);udp.Send(t,t.Length,"127.0.0.1",port);foreach(Bone bone in frames[index].bones){float[] v=(float[])bone.values.Clone();if(bone.name=="Hips"){double a=c.yaw*Math.PI/360;float sy=(float)Math.Sin(a),cy=(float)Math.Cos(a);float x=v[3],y=v[4],z=v[5],w=v[6];v[3]=cy*x+sy*z;v[4]=cy*y+sy*w;v[5]=cy*z-sy*x;v[6]=cy*w-sy*y;}Smooth(bone.name,v);byte[] packet=Message("/VMC/Ext/Bone/Pos",bone.name,v);udp.Send(packet,packet.Length,"127.0.0.1",port);}}Thread.Sleep(33);}udp.Dispose();Log("STOP");return 0;}catch(Exception e){Log("FATAL "+e);return 1;}}
}
