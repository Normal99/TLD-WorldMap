using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldMap
{
    // The item keeps the game's pickup/save identity. Its entire visible model is
    // a folded sheet in item space: no detached page or camera-facing geometry.
    public sealed class HeldMapDisplay : MonoBehaviour
    {
        private static Mesh _sheetMesh;
        private static Material _frontMaterial, _backMaterial;
        private static Sprite _inventoryIcon;
        private Renderer[] _stockRenderers;
        private MeshRenderer _paper;
        private pickupable _pickup;
        private layerScript _paperLayer;

        private void Awake()
        {
            _pickup = GetComponent<pickupable>();
            if (_pickup == null) return;
            EnsureAssets();
            _stockRenderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in _stockRenderers) r.enabled = false;

            int worldLayer = _pickup.cols != null && _pickup.cols.Count > 0
                ? _pickup.cols[0].gameObject.layer : gameObject.layer;
            PhysicMaterial physics = null;
            foreach (var col in GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger) continue;
                if (physics == null) physics = col.sharedMaterial;
                col.enabled = false;
            }

            var sheet = new GameObject("FoldedFieldMap");
            sheet.transform.SetParent(transform, false);
            sheet.layer = worldLayer;
            sheet.AddComponent<MeshFilter>().sharedMesh = _sheetMesh;
            _paper = sheet.AddComponent<MeshRenderer>();
            _paper.sharedMaterials = new[] { _frontMaterial, _backMaterial };
            _paper.shadowCastingMode = ShadowCastingMode.TwoSided;
            _paper.receiveShadows = true;

            // One collider also supplies the game's layer/pickup integration.
            // Keep a little thickness so a dropped folded sheet rests reliably.
            var collider = sheet.AddComponent<BoxCollider>();
            collider.center = _sheetMesh.bounds.center;
            var size = _sheetMesh.bounds.size;
            collider.size = new Vector3(size.x, size.y, Mathf.Max(.012f, size.z));
            collider.sharedMaterial = physics;
            _paperLayer = sheet.AddComponent<layerScript>();
            _paperLayer.FStart(_pickup, 0);
            _pickup.cols = new List<layerScript> { _paperLayer };
            _pickup.invImg = _inventoryIcon;
            _pickup.disableRHFix = false;
            _pickup.RHR = new Vector3(12f, -10f, -6f);
            // The sheet pivots at its centre for loose pickup and rotation. The
            // equipped offset puts its lower-right corner at the game's grip.
            _pickup.RHP = new Vector3(-.015f, .015f, .02f) +
                Quaternion.Euler(_pickup.RHR) * new Vector3(-.125f, .099375f, 0f);
        }

        private void LateUpdate()
        {
            if (_paper == null) return;
            // Condition/paint refreshes can enable stock mesh renderers again.
            foreach (var r in _stockRenderers) if (r != null) r.enabled = false;
            // The game changes this object's layer on pickup/equip/store/drop.
            // The visual and collider share that object, so they move together.
            // Only the map in this player's hands draws the live map (all copies share the material, so one lying in
            // the house showed it too, rebuilding it every 0.3 s: isVisible is also true for the shadow pass).
            var pl = mainscript.s != null ? mainscript.s.player : null;
            if (pl != null && _paper.isVisible && (pl.pickedUp == _pickup || pl.inHandP == _pickup)) _frontMaterial.mainTexture = MapSession.GetTexture();
        }

        private static void EnsureAssets()
        {
            if (_sheetMesh != null) return;
            _sheetMesh = CreateSheet();
            var shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Texture");
            _frontMaterial = new Material(shader) { name = "FieldMapPaper" };
            _frontMaterial.SetFloat("_Glossiness", 0f);
            _frontMaterial.SetFloat("_Metallic", 0f);
            _frontMaterial.mainTexture = MapSession.GetTexture();
            // A small amount of the same ink remains legible in dark interiors.
            if (_frontMaterial.HasProperty("_EmissionColor"))
            {
                _frontMaterial.EnableKeyword("_EMISSION");
                _frontMaterial.SetColor("_EmissionColor", new Color(.18f, .18f, .18f));
                _frontMaterial.SetTexture("_EmissionMap", _frontMaterial.mainTexture);
            }
            _backMaterial = new Material(shader) { name = "FieldMapReverse", color = new Color(.88f, .84f, .74f) };
            _backMaterial.SetFloat("_Glossiness", 0f);
            _backMaterial.mainTexture = MapSession.GetReverseTexture();
            if (_backMaterial.HasProperty("_EmissionColor"))
            {
                _backMaterial.EnableKeyword("_EMISSION");
                _backMaterial.SetColor("_EmissionColor", new Color(.12f, .12f, .12f));
                _backMaterial.SetTexture("_EmissionMap", _backMaterial.mainTexture);
            }
            _inventoryIcon = CreateIcon();
        }

        private static Mesh CreateSheet()
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var front = new List<int>();
            var back = new List<int>();
            // A small accordion fold, a cross crease and slightly bent corners.
            float[] folds = { 0f, .001f, .009f, -.002f, .008f, .002f, 0f };
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < 6; x++)
            {
                float u0 = x / 6f, u1 = (x + 1) / 6f;
                float v0 = y / 2f, v1 = (y + 1) / 2f;
                var a = SheetPoint(u0, v0, folds[x]);
                var b = SheetPoint(u0, v1, folds[x]);
                var c = SheetPoint(u1, v1, folds[x + 1]);
                var d = SheetPoint(u1, v0, folds[x + 1]);
                AddFace(vertices, uvs, front, a, b, c, d, u0, v0, u1, v1);
                var thickness = Vector3.forward * .001f;
                // seen from behind, the panels run the other way: the reverse page reads left to right
                AddFace(vertices, uvs, back, d + thickness, c + thickness, b + thickness, a + thickness, 1f - u1, v0, 1f - u0, v1);
                if (y == 0) AddFace(vertices, uvs, back, d, d + thickness, a + thickness, a, 0, 0, 1, 1);
                if (y == 1) AddFace(vertices, uvs, back, b, b + thickness, c + thickness, c, 0, 0, 1, 1);
                if (x == 0) AddFace(vertices, uvs, back, a, a + thickness, b + thickness, b, 0, 0, 1, 1);
                if (x == 5) AddFace(vertices, uvs, back, c, c + thickness, d + thickness, d, 0, 0, 1, 1);
            }
            var mesh = new Mesh { name = "FoldedFieldMapMesh" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(front, 0);
            mesh.SetTriangles(back, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 SheetPoint(float u, float v, float fold)
        {
            return new Vector3(Mathf.Lerp(-.15f, .15f, u), Mathf.Lerp(-.084375f, .084375f, v),
                fold + (v == .5f ? .004f : 0f));
        }

        private static void AddFace(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u0, float v0, float u1, float v1)
        {
            int start = vertices.Count;
            vertices.AddRange(new[] { a, b, c, d });
            uvs.AddRange(new[] { new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0) });
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        private static Sprite CreateIcon()
        {
            const int w = 128, h = 96;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "FieldMapIcon" };
            var pixels = new Color32[w * h];
            for (int y = 10; y < 86; y++)
            for (int x = 8; x < 120; x++)
            {
                int edge = Mathf.Min(Mathf.Min(x - 8, 119 - x), Mathf.Min(y - 10, 85 - y));
                byte shade = (byte)(edge < 2 || x == 45 || x == 82 ? 133 : 217 + (x * 17 + y * 13) % 9);
                pixels[y * w + x] = new Color32(shade, (byte)(shade * .93f), (byte)(shade * .76f), 255);
            }
            for (int x = 23; x < 104; x++)
            {
                int y = 34 + (int)(Mathf.Sin(x * .1f) * 11) + x / 5;
                for (int k = -1; k <= 1; k++) pixels[(y + k) * w + x] = new Color32(139, 65, 43, 255);
            }
            texture.SetPixels32(pixels); texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100f);
        }
    }
}
