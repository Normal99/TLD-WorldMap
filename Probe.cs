using System.Collections.Generic;
using System.Globalization;
using TLDRevamp.Multiplayer;
using UnityEngine;

namespace WorldMap
{
    /// Test hooks for RevaMP's debug bridge (`call WorldMap.Probe.State`): what a player would see, as text.
    public static class Probe
    {
        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// Holding, the open view, the cursor, the sheet's place on the screen and its facing, the map's contents.
        public static string State()
        {
            var pl = mainscript.s != null ? mainscript.s.player : null;
            if (pl == null) return "no player";
            var held = MapItemRegistration.IsMap(pl.inHandP) ? pl.inHandP : MapItemRegistration.IsMap(pl.pickedUp) ? pl.pickedUp : null;
            string how = held == null ? "none" : held == pl.inHandP ? "hand" : "picked";
            var parts = new List<string>
            {
                "holding " + how, "open " + MapSession.IsOpen, "cursor " + Cursor.lockState + " visible " + Cursor.visible + " crsrLocked " + mainscript.s.crsrLocked,
                MapSession.Describe()
            };
            if (held != null) parts.Add(Sheet(held, pl));
            return string.Join("; ", parts);
        }

        /// Every map item in the world: where it is, whether its stock book shows, its collider.
        public static string Items()
        {
            var rows = new List<string>();
            foreach (var d in Object.FindObjectsOfType<HeldMapDisplay>())
            {
                int stock = 0; foreach (var r in d.GetComponentsInChildren<Renderer>(true)) if (r.enabled && r.name != "FoldedFieldMap") stock++;
                int sheets = 0; foreach (Transform c in d.transform) if (c.name == "FoldedFieldMap") sheets++;
                var rb = d.GetComponent<Rigidbody>();
                rows.Add($"{d.name} at {d.transform.position} sheets {sheets} stock renderers on {stock} kinematic {(rb != null && rb.isKinematic)} sleeping {(rb != null && rb.IsSleeping())}");
            }
            return rows.Count == 0 ? "none" : string.Join(" | ", rows);
        }

        /// The nearest map put in front of the eyes, still (a player picks it up from there: one lying on the floor
        /// further than the game's dropDist from the hold point drops again at once).
        public static string Bring()
        {
            var pl = mainscript.s.player; var cam = pl.Cam.transform;
            HeldMapDisplay best = null; float bd = float.MaxValue;
            foreach (var d in Object.FindObjectsOfType<HeldMapDisplay>())
            {
                float dd = (d.transform.position - pl.transform.position).sqrMagnitude;
                if (dd < bd) { bd = dd; best = d; }
            }
            if (best == null) return "no map";
            var rb = best.GetComponent<Rigidbody>();
            best.transform.SetPositionAndRotation(cam.position + cam.forward * .6f, Quaternion.LookRotation(cam.forward, cam.up));   // the printed side (-Z) to the eyes
            if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            return "brought " + best.name;
        }

        /// Every trail: whose (a name, "You" for this player's own), how many points; the live markers.
        public static string Trails() => MapSession.TrailList();

        public static string Save() { MapSession.SaveForTest(); return MapSession.Describe(); }
        public static string Open() { MapSession.SetOpenForTest(true); return State(); }
        public static string Close() { MapSession.Close(); return State(); }
        public static string Zoom(string factor) { MapSession.ZoomForTest(float.Parse(factor, CultureInfo.InvariantCulture)); return MapSession.Describe(); }
        public static string Pan(string dx, string dz) { MapSession.PanForTest(double.Parse(dx, CultureInfo.InvariantCulture), double.Parse(dz, CultureInfo.InvariantCulture)); return MapSession.Describe(); }

        private static string Sheet(pickupable held, fpscontroller pl)
        {
            var sheet = held.transform.Find("FoldedFieldMap");
            var r = sheet != null ? sheet.GetComponent<MeshRenderer>() : null;
            var cam = pl.Cam;
            if (r == null || cam == null) return "sheet missing";
            // the sheet's own corners (the renderer's world box is much larger than a tilted sheet)
            var b = sheet.GetComponent<MeshFilter>().sharedMesh.bounds;
            float x0 = 9, y0 = 9, x1 = -9, y1 = -9; bool behind = false;
            for (int i = 0; i < 8; i++)
            {
                var c = sheet.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                var v = cam.WorldToViewportPoint(c);
                if (v.z <= cam.nearClipPlane) behind = true;
                x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x); y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
            }
            // The printed side faces -Z of the sheet (the mesh's front faces): how squarely it faces the eye.
            var toEye = (cam.transform.position - sheet.position).normalized;
            float faces = Vector3.Dot(-sheet.forward, toEye);
            return $"sheet visible {r.isVisible} layer {sheet.gameObject.layer} viewport x {F(x0)}..{F(x1)} y {F(y0)}..{F(y1)} behind {behind} faces {F(faces)} dist {F(Vector3.Distance(cam.transform.position, sheet.position))}";
        }
    }
}
