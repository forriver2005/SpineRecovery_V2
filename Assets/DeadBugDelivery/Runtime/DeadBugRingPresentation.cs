using TMPro;
using UnityEngine;

namespace DeadBugDelivery
{
    public sealed class DeadBugRingPresentation : MonoBehaviour
    {
        public GameObject artwork;
        public TMP_FontAsset font;
        public Material spriteMaterial;
        public Sprite[] noteBodies;
        readonly SpriteRenderer[] flashes = new SpriteRenderer[4], notes = new SpriteRenderer[2];
        readonly Color[] colors = { new Color32(255,194,210,255), new Color32(255,234,168,255), new Color32(180,240,229,255), Color.white };
        float flashTime;
        DeadBugSide flashSide;

        public void Initialize()
        {
            string[] zones = { "melody", "harmony", "bass", "drums" }, labels = { "左臂", "右臂", "左腿", "右腿" };
            Rect[] glyphs = { new Rect(358,346,53,58), new Rect(792,345,42,60), new Rect(351,789,72,36), new Rect(786,766,54,55) };
            float scale = 0;
            for (int i = 0; i < 4; i++)
            {
                var zone = artwork.transform.Find("Ring/Zone_" + zones[i]);
                var source = zone.Find("Base").GetComponent<SpriteRenderer>(); var sprite = source.sprite; var glyph = glyphs[i];
                Vector2 Uv(float x, float y) => new Vector2((sprite.textureRect.x + x / 1200 * sprite.textureRect.width) / sprite.texture.width,
                    (sprite.textureRect.y + (1 - y / 1200) * sprite.textureRect.height) / sprite.texture.height);
                var min = Uv(glyph.xMin - 4, glyph.yMax + 4); var max = Uv(glyph.xMax + 4, glyph.yMin - 4);
                var fill = Uv(glyph.xMin - 6, glyph.yMin - 6); var centerUV = Uv(600,600); var edge = Uv(940,260);
                var block = new MaterialPropertyBlock(); source.GetPropertyBlock(block);
                block.SetVector("_SymbolRect", new Vector4(min.x,min.y,max.x,max.y));
                block.SetVector("_SymbolFillUV", new Vector4(fill.x,fill.y,0,0));
                block.SetVector("_SymbolCircle", new Vector4(centerUV.x,centerUV.y,edge.x-centerUV.x,edge.y-centerUV.y)); source.SetPropertyBlock(block);
                var text = new GameObject("Limb label " + labels[i]).AddComponent<TextMeshPro>(); text.transform.SetParent(source.transform,false);
                text.gameObject.layer = source.gameObject.layer; text.font = font; text.text = labels[i]; text.fontSize = 10; text.color = colors[i];
                text.alignment = TextAlignmentOptions.Center; text.enableWordWrapping = false; text.rectTransform.sizeDelta = Vector2.one * 10; text.ForceMeshUpdate();
                if (i == 0) scale = 1.3f * (glyph.width / 1200 * sprite.rect.width / sprite.pixelsPerUnit) / text.textBounds.size.x;
                Vector3 center = (Vector2.Scale(new Vector2(glyph.center.x / 1200, 1-glyph.center.y / 1200),sprite.rect.size)-sprite.pivot)/sprite.pixelsPerUnit;
                text.transform.localScale = Vector3.one * scale; text.transform.localPosition = center-text.textBounds.center*scale;
                var renderer = text.GetComponent<MeshRenderer>(); renderer.sortingLayerID = source.sortingLayerID; renderer.sortingOrder = source.sortingOrder + 1;
                flashes[i] = zone.Find("HitFlash").GetComponent<SpriteRenderer>(); flashes[i].color = Color.clear;
            }
            for (int i = 0; i < 2; i++)
            {
                var note = new GameObject("User diagonal note").AddComponent<SpriteRenderer>(); note.transform.SetParent(artwork.transform,false);
                note.gameObject.layer = 30; note.sharedMaterial = spriteMaterial; note.sortingOrder = 80;
                note.transform.localScale = Vector3.one * .65f; notes[i] = note; note.enabled = false;
            }
        }

        public void Hit(DeadBugSide side) { flashTime = .8f; flashSide = side; }
        public void Render(DeadBugHoldScorer scorer, DeadBugPose input, float dt, bool running)
        {
            flashTime = Mathf.Max(0,flashTime-dt);
            for (int i = 0; i < 4; i++) flashes[i].color = new Color(1,1,1, PairContains(flashSide,i) ? flashTime : 0);
            DeadBugSide side = scorer.LockedSide;
            if (side == DeadBugSide.None) side = Mathf.Max(input.LeftArm.Height,input.RightLeg.Height) < Mathf.Max(input.RightArm.Height,input.LeftLeg.Height)
                ? DeadBugSide.LeftArmRightLeg : DeadBugSide.RightArmLeftLeg;
            int arm = side == DeadBugSide.LeftArmRightLeg ? 0 : 1, leg = side == DeadBugSide.LeftArmRightLeg ? 3 : 2;
            float radius = Mathf.Lerp(2.95f,1.43f,scorer.CurrentProgress);
            for (int i = 0; i < 2; i++)
            {
                int zone = i == 0 ? arm : leg;
                notes[i].enabled = running && scorer.TrackingAvailable && (scorer.Phase == HipThrustPhase.Raising || scorer.Phase == HipThrustPhase.Holding);
                notes[i].sprite = noteBodies[zone]; notes[i].color = colors[zone];
                float angle = (zone == 0 ? 135 : zone == 1 ? 45 : zone == 2 ? 225 : 315) * Mathf.Deg2Rad;
                notes[i].transform.localPosition = new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0)*radius;
            }
        }
        static bool PairContains(DeadBugSide side,int i) => side == DeadBugSide.LeftArmRightLeg ? i == 0 || i == 3 : side == DeadBugSide.RightArmLeftLeg && (i == 1 || i == 2);
    }
}
