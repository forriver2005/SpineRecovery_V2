using UnityEngine;

/// <summary>Music follows each game's existing preparation/play/pause/result lifecycle.</summary>
[DefaultExecutionOrder(500)]
[RequireComponent(typeof(AudioSource))]
public sealed class GameTrainingMusic : MonoBehaviour
{
    public GameWorkoutScene entry;
    public AudioClip clip;
    [Range(0,1)]public float volume=.28f;
    AudioSource source;bool playing,paused;
    public AudioSource Source=>source;
    void Awake(){source=GetComponent<AudioSource>();source.playOnAwake=false;source.loop=true;source.spatialBlend=0;source.volume=volume;source.clip=clip;}
    void LateUpdate()
    {
        bool active=false,freeze=false;
        if(entry.bridgeWave!=null)
        {
            var state=entry.bridgeWave.State;active=state==BridgeWave.BridgeWaveFollowController.RunState.Countdown||state==BridgeWave.BridgeWaveFollowController.RunState.Playing||state==BridgeWave.BridgeWaveFollowController.RunState.Paused;
            freeze=state==BridgeWave.BridgeWaveFollowController.RunState.Paused;
        }
        else if(entry.taikoNightAction!=null){active=entry.taikoNightAction.IsRunning||entry.taikoNightAction.IsCountingDown;freeze=TaikoNight.TaikoNightPracticeClock.IsPaused;}
        if(!active){if(playing)source.Stop();playing=paused=false;return;}
        if(!playing){source.Play();playing=true;}
        if(freeze&&!paused){source.Pause();paused=true;}else if(!freeze&&paused){source.UnPause();paused=false;}
    }
    void OnDisable(){if(source!=null)source.Stop();playing=paused=false;}
}
