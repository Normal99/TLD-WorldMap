using UnityEngine;
using UnityEngine.Rendering;

namespace WorldMap
{
    // A small, readable map page on the actual held item. The game's book still
    // supplies its normal pickup, inventory, save and multiplayer behaviour.
    internal sealed class HeldMapDisplay : MonoBehaviour
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
            if (!held) { if (_renderer != null) _renderer.enabled = false; return; }

            if (_page == null) CreatePage();
            if (_renderer == null || player.mainCam == null) return;
            _renderer.enabled = true;
            _material.mainTexture = MapSession.GetTexture();
            UpdateBookBounds();

            // Face the local camera so the map is legible with either hand pose.
            // Keep it directly on the book's visible face, ahead of its bounds.
            var towardsCamera = (player.mainCam.transform.position - _bookBounds.center).normalized;
            float depth = Vector3.Dot(new Vector3(
                Mathf.Abs(towardsCamera.x), Mathf.Abs(towardsCamera.y), Mathf.Abs(towardsCamera.z)),
                _bookBounds.extents);
            _page.transform.position = _bookBounds.center + towardsCamera * (depth + .008f);
            _page.transform.rotation = Quaternion.LookRotation(towardsCamera, player.mainCam.transform.up);
            _page.transform.localScale = new Vector3(_pageWidth, _pageWidth * 500f / 720f, 1f);
        }

        private void CreatePage()
        {
            _bookRenderers = GetComponentsInChildren<Renderer>();
            UpdateBookBounds();
            _pageWidth = Mathf.Clamp(Mathf.Max(_bookBounds.size.x, _bookBounds.size.y, _bookBounds.size.z) * .9f, .12f, .35f);

            var shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                BepInEx.Logging.Logger.CreateLogSource("WorldMap").LogWarning("No shader found for held map page");
                enabled = false;
                return;
            }
            _page = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _page.name = "WorldMapPage";
            _page.layer = gameObject.layer;
            _page.transform.SetParent(transform, true);
            var collider = _page.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; Destroy(collider); }
            _renderer = _page.GetComponent<MeshRenderer>();
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
        }
    }
}
