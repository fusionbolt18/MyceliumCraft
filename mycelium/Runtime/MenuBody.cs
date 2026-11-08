using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// The shop's skill menu shows your Minecraft player instead of the spore (your request,
    /// 2026-11-08).
    ///
    /// Mycelium's skill menu (SkillMenuManagerUpdated.InstantiateCurrentSpore) shows a copy of your
    /// spore (InstantiatedSpore): made bigger, turned to the menu's camera, on the "UI" layer that only
    /// that camera draws. While it's there, its renderers are switched off (forceRenderingOff), and a
    /// mesh of its own is drawn in its place: your Minecraft body as Minecraft last sent it
    /// (McPlayerModel's mesh and materials, so it moves and wears what you wear).
    ///
    /// The body comes already turned the way it faces in Minecraft, which is the way your spore faces
    /// in Mycelium. So it's turned by the copy's turn less your spore's, and sized by the copy's size
    /// over your spore's. It stands where the copy's feet are.
    ///
    /// Swapping spores in the menu (SwapNext / SwapLast, from its buttons) makes a new copy later in
    /// the frame than this Update, so the new copy was drawn for a frame before Update hid it (your
    /// report, 2026-11-08: the Mycelium spore for a split second). So a new copy is also hidden
    /// just before any camera draws (RenderPipelineManager.beginCameraRendering), while your body is
    /// being shown there.
    /// </summary>
    sealed class MenuBody
    {
        Type menuType;
        FieldInfo sporeField;
        Component menu;
        float nextLook;
        GameObject copy;
        GameObject body;
        MeshFilter filter;
        MeshRenderer bodyRenderer;
        readonly List<Renderer> under = new List<Renderer>();
        bool logged;
        bool hooked, ready;
        GameObject caughtCopy;

        /// <summary>For the test aid: copies hidden just before drawing, not yet by Update.</summary>
        public int CaughtBeforeDrawing { get; private set; }

        /// <summary>Every frame, after the player model's update. feetOffset: a spore's middle to its feet.</summary>
        public void Update(McPlayerModel model, float feetOffset)
        {
            if (!hooked)
            {
                hooked = true;
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += BeforeDrawing;
            }
            GameObject shown = MenuSpore();
            Mesh mesh = model.BodyMesh;
            GameObject spore = GameObject.FindWithTag("currentPlayer");
            ready = mesh != null && spore != null && McConfig.PlayerModel;
            if (shown == null || !ready)
            {
                Hide(shown == null ? "the skill menu's spore is gone" : mesh == null ? "no Minecraft body yet" : "no spore");
                return;
            }
            if (shown != copy)
            {
                copy = shown;
                logged = false;
            }
            caughtCopy = copy;
            if (body == null) Create();

            // The copy: not drawn (Mycelium may turn renderers on again, so every frame).
            copy.GetComponentsInChildren(true, under);
            foreach (Renderer r in under) if (r != bodyRenderer) r.forceRenderingOff = true;

            filter.sharedMesh = mesh;
            bodyRenderer.sharedMaterials = model.BodyMaterials;
            body.layer = copy.layer;
            float size = spore.transform.lossyScale.y > 1e-4f ? copy.transform.lossyScale.y / spore.transform.lossyScale.y : 1f;
            float turn = copy.transform.eulerAngles.y - spore.transform.eulerAngles.y;
            body.transform.SetPositionAndRotation(copy.transform.position - Vector3.up * feetOffset * size, Quaternion.Euler(0f, turn, 0f));
            body.transform.localScale = Vector3.one * size;
            if (!body.activeSelf) body.SetActive(true);
            if (!logged)
            {
                logged = true;
                Vector3 p = body.transform.position;
                // Which way the body faces (your spore's facing, turned as the copy is) against the camera that draws the menu.
                Vector3 facing = Quaternion.Euler(0f, turn, 0f) * spore.transform.forward;
                string toCamera = "the menu has no camera";
                var camObject = menu.GetType().GetField("Camera", BindingFlags.Public | BindingFlags.Instance)?.GetValue(menu) as GameObject;
                Camera cam = camObject != null ? camObject.GetComponentInChildren<Camera>(true) ?? Camera.main : null;
                if (cam != null)
                {
                    Vector3 to = cam.transform.position - p;
                    toCamera = $"{Vector3.SignedAngle(new Vector3(facing.x, 0f, facing.z), new Vector3(to.x, 0f, to.z), Vector3.up):F0} deg off the direction to camera '{cam.name}' (menu's '{camObject.name}', draws its layer: {(cam.cullingMask & (1 << copy.layer)) != 0})";
                }
                McLog.Info($"skill menu: your Minecraft body in place of the spore '{copy.name}' ({under.Count} of its renderers off), " +
                           $"at ({p.x:F2}, {p.y:F2}, {p.z:F2}), {size:F2}x, turned {turn:F0} deg, facing {toCamera}, layer {LayerMask.LayerToName(copy.layer)}, {mesh.vertexCount / 3} triangles");
            }
        }

        /// <summary>
        /// Just before a camera draws: a copy the menu made since this frame's Update (a spore swap)
        /// gets its renderers off now, so it's never drawn. Update puts your body there next frame.
        /// </summary>
        void BeforeDrawing(UnityEngine.Rendering.ScriptableRenderContext context, Camera cam)
        {
            if (!ready || menu == null || !(menu is Behaviour b) || !b.isActiveAndEnabled) return;
            var shown = sporeField.GetValue(menu) as GameObject;
            if (shown == null || shown == caughtCopy) return;
            caughtCopy = shown;
            shown.GetComponentsInChildren(true, under);
            int on = 0;
            foreach (Renderer r in under)
            {
                if (r == bodyRenderer || r.forceRenderingOff) continue;
                r.forceRenderingOff = true;
                on++;
            }
            CaughtBeforeDrawing++;
            McLog.Info($"skill menu: a new spore copy '{shown.name}' (a swap) hidden just before camera '{cam.name}' drew it, frame {Time.frameCount} ({on} renderers were on)");
        }

        void Hide(string why)
        {
            if (body != null && body.activeSelf)
            {
                body.SetActive(false);
                McLog.Info($"skill menu: your Minecraft body there hidden ({why})");
            }
            copy = null;
            caughtCopy = null;
        }

        void Create()
        {
            body = new GameObject("MyceliumCraft skill menu body");
            UnityEngine.Object.DontDestroyOnLoad(body);
            filter = body.AddComponent<MeshFilter>();
            bodyRenderer = body.AddComponent<MeshRenderer>();
            bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bodyRenderer.receiveShadows = false;
        }

        /// <summary>The skill menu's copy of your spore while it's shown, or null.</summary>
        GameObject MenuSpore()
        {
            if (menu == null)
            {
                if (Time.realtimeSinceStartup < nextLook) return null;
                nextLook = Time.realtimeSinceStartup + 1f;
                if (menuType == null)
                {
                    menuType = Type.GetType("SkillMenuManagerUpdated, Assembly-CSharp");
                    sporeField = menuType?.GetField("InstantiatedSpore", BindingFlags.Public | BindingFlags.Instance);
                }
                if (sporeField == null) return null;
                // The menu's object is switched off while it's shut: look among those too.
                foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(menuType))
                    if (o is Component c && c.gameObject.scene.IsValid()) { menu = c; break; }
                if (menu == null) return null;
                McLog.Info($"skill menu: found Mycelium's skill menu on '{menu.gameObject.name}'");
            }
            if (!(menu is Behaviour b) || !b.isActiveAndEnabled) return null;
            var spore = sporeField.GetValue(menu) as GameObject;
            return spore != null && spore.activeInHierarchy ? spore : null;
        }

        /// <summary>A new scene: its menu is a new one.</summary>
        public void SceneLoaded()
        {
            Hide("new scene");
            menu = null;
            nextLook = 0f;
        }

        /// <summary>For the test aid.</summary>
        public Component Menu => menu;
        public bool Showing => body != null && body.activeSelf;
    }
}
