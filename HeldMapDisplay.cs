using UnityEngine;
using UnityEngine.Rendering;

namespace WorldMap
{
    // A small, readable map page on the actual held item. The game's book still
    // supplies its normal pickup, inventory, save and multiplayer behaviour.
    public sealed class HeldMapDisplay : MonoBehaviour
    {
        private GameObject _page;
        private MeshRenderer _renderer;
        private Material _material;
        private Bounds _bookBounds;
        private Renderer[] _bookRenderers;
        private float _pageWidth;

        private void LateUpdate()
        {
            var player = mainscript.s != null ? mainscript.s.player : null;
            bool held = player != null &&
                (player.pickedUp != null && player.pickedUp.gameObject == gameObject ||
                 player.inHandP != null && player.inHandP.gameObject == gameObject);
            if (!held)
            {
                if (_renderer != null) _renderer.enabled = false;
                return;
            }

            if (_page == null) CreatePage();
            if (_renderer == null || player.mainCam == null) return;
            _renderer.enabled = true;
            _material.mainTexture = MapSession.GetTexture();
            UpdateBookBounds();

            if (player.inHandP != null && player.inHandP.gameObject == gameObject)
            {
                // The stock book's right-hand grip sits partly outside the
                // camera. Display its unfolded page above the grip instead.
                var cam = player.Cam != null ? player.Cam.transform : player.mainCam.transform;
                _page.transform.position = cam.position + cam.forward * .7f + cam.right * .22f - cam.up * .08f;
                _page.transform.rotation = Quaternion.LookRotation(-cam.forward, cam.up);
                _page.transform.localScale = new Vector3(.46f, .46f * 540f / 960f, 1f);
                return;
            }

            // Face the local camera so the map is legible with either hand pose.
            // Keep it directly on the book's visible face, ahead of its bounds.
            var towardsCamera = (player.mainCam.transform.position - _bookBounds.center).normalized;
            float depth = Vector3.Dot(new Vector3(
                Mathf.Abs(towardsCamera.x), Mathf.Abs(towardsCamera.y), Mathf.Abs(towardsCamera.z)),
                _bookBounds.extents);
            _page.transform.position = _bookBounds.center + towardsCamera * (depth + .08f);
            _page.transform.rotation = Quaternion.LookRotation(towardsCamera, player.mainCam.transform.up);
            // Right-hand grip brings the item much nearer the camera than loose
            // pickup. Cap its projected size so the paper stays inside the view.
            float pageWidth = Mathf.Min(_pageWidth,
                Mathf.Max(.08f, Vector3.Distance(player.mainCam.transform.position, _page.transform.position) * .55f));
            _page.transform.localScale = new Vector3(pageWidth, pageWidth * 540f / 960f, 1f);
        }

        private void CreatePage()
        {
            _bookRenderers = GetComponentsInChildren<Renderer>();
            UpdateBookBounds();
            _pageWidth = Mathf.Clamp(Mathf.Max(_bookBounds.size.x, _bookBounds.size.y, _bookBounds.size.z) * 1.5f, .38f, .58f);

            var shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                BepInEx.Logging.Logger.CreateLogSource("WorldMap").LogWarning("No shader found for held map page");
                enabled = false;
                return;
            }
            _page = new GameObject("WorldMapPage");
            _page.name = "WorldMapPage";
            _page.layer = mainscript.pickedUpLayer;
            // The game's pickup cameras can see the opposite side of a stock Quad.
            // Draw both faces explicitly, with the same paper UVs on each side.
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-.5f,-.5f), new Vector3(.5f,-.5f), new Vector3(.5f,.5f), new Vector3(-.5f,.5f) };
            mesh.uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) };
            mesh.triangles = new[] { 0,1,2, 0,2,3, 2,1,0, 3,2,0 };
            mesh.RecalculateNormals();
            _page.AddComponent<MeshFilter>().sharedMesh = mesh;
            _renderer = _page.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _material = new Material(shader);
            _renderer.sharedMaterial = _material;
        }

        private void UpdateBookBounds()
        {
            bool found = false;
            foreach (var r in _bookRenderers)
            {
                if (r == null || !r.enabled) continue;
                if (!found) { _bookBounds = r.bounds; found = true; }
                else _bookBounds.Encapsulate(r.bounds);
            }
            if (!found) _bookBounds = new Bounds(transform.position, Vector3.one * .18f);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_page != null) Destroy(_page);
        }
    }
}
