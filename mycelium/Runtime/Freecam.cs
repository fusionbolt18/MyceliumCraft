using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MyceliumCraft
{
    /// <summary>
    /// A free camera (your request, 2026-11-11), only when Mycelium is started with "-freecam".
    /// '\' switches it on and off. While on, Mycelium's camera stays where it was and flies with
    /// I / K (forward / back), J / L (left / right), O / U (up / down; swapped from U / O, your request 2026-11-11), relative to where it faced when
    /// switched on (forward and back stay level). PageUp / PageDown make it faster / slower
    /// (x1.5 a press). The arrow keys turn it (left / right) and tilt it (up / down, to 89 degrees),
    /// 90 degrees a second (your request, 2026-11-11). Holding the right mouse button, the mouse turns
    /// it instead (0.15 degrees a pixel; your request, 2026-11-11): while the freecam is on the right
    /// button and the mouse's movement aren't sent to Minecraft, and Mycelium's own actions bound to
    /// the right button (Interact, by default) are off. You keep playing meanwhile: Minecraft still gets
    /// every other key and the mouse, and your Minecraft body is drawn whole even in first person.
    ///
    /// With -freecam those 13 keys are always Mycelium's (Keys): the input bridge never sends them to
    /// Minecraft (Minecraft's L, advancements, for example, doesn't open). The camera is set just
    /// before Mycelium's main camera draws (RenderPipelineManager.beginCameraRendering), after
    /// Cinemachine and CameraSync, so nothing else moves it.
    /// </summary>
    sealed class Freecam
    {
        /// <summary>SDL scancodes of the freecam's keys: \ I J K L U O PageUp PageDown, and the arrows (right, left, down, up).</summary>
        public static readonly int[] Scancodes = { 49, 12, 13, 14, 15, 24, 18, 75, 78, 79, 80, 81, 82, 76, 73 }; // 76: Delete, 73: Insert
        /// <summary>Del (with -freecam, freecam on or off): every spore but yours hidden, skills and all (McColonyBodies).</summary>
        public bool OthersHidden { get; private set; }
        const float TurnSpeed = 90f; // degrees per second
        const float MouseTurn = 0.15f; // degrees per pixel, with the right button held
        static readonly System.Type ControllerType = System.Type.GetType("PlayerController, Assembly-CSharp");
        static readonly System.Reflection.FieldInfo ActionsField = ControllerType?.GetField("playerActionsAsset");
        readonly List<InputAction> heldOff = new List<InputAction>();
        float nextHoldOff;
        bool mouseLooking;
        /// <summary>The right button is held with the freecam on: the mouse turns it (InputBridge keeps it from Minecraft).</summary>
        public bool MouseLooking => Active && mouseLooking;
        float yaw, pitch;
        const float DefaultSpeed = 5f; // Mycelium units per second
        const float MinSpeed = 0.25f, MaxSpeed = 200f;

        public readonly bool Allowed = Array.IndexOf(Environment.GetCommandLineArgs(), "-freecam") >= 0;
        public bool Active { get; private set; }
        Vector3 position;
        Quaternion rotation;
        float speed = DefaultSpeed;
        bool hooked, loggedAllowed;
        float nextLog;
        Vector3 loggedFrom;
        float loggedYaw, loggedPitch;

        struct MenuHidden { public CanvasGroup group; public bool added; public float alpha; public bool interactable, raycasts; }
        readonly List<MenuHidden> menuOff = new List<MenuHidden>();
        bool menuHidden, menuTested;
        int menuTest = -1;

        /// <summary>
        /// F1 on Mycelium's main menu, with -freecam (your request, 2026-11-13): the menu's buttons go,
        /// the logo stays (your correction, the same day). The menu's canvas ("Canvas") holds the logo,
        /// its background and the panels (MainMenu's buttons, About, Profile, Mode Select); every child
        /// but "Logo" and "...Background" is made invisible and unclickable with a CanvasGroup (alpha 0),
        /// so a panel opened later stays hidden too. F1 again, or leaving the menu, puts it all back.
        /// Called every frame, linked to Minecraft or not.
        /// Test aid: "-mcMenuF1Test" hides them 4 s after start and shows them again 3 s later.
        /// </summary>
        public void MainMenuF1()
        {
            if (!Allowed) return;
            bool onMenu = SceneManager.GetActiveScene().name == "Main Menu";
            if (!onMenu)
            {
                if (menuHidden) { menuHidden = false; menuOff.Clear(); McLog.Info("freecam: left the main menu; its buttons are back"); }
                return;
            }
            Keyboard kb = Keyboard.current;
            bool toggle = kb != null && kb.f1Key.wasPressedThisFrame && Application.isFocused;
            string why = "F1";
            if (menuTest < 0) menuTest = Array.IndexOf(Environment.GetCommandLineArgs(), "-mcMenuF1Test") >= 0 ? 1 : 0;
            if (menuTest == 1 && !menuTested && Time.realtimeSinceStartup > 4f && !menuHidden) { toggle = true; why = "test aid"; }
            if (menuTest == 1 && !menuTested && menuHidden && Time.realtimeSinceStartup > 7f) { toggle = true; why = "test aid"; menuTested = true; }
            if (!toggle) return;
            if (menuHidden)
            {
                int back = 0;
                foreach (MenuHidden h in menuOff)
                {
                    if (h.group == null) continue;
                    if (h.added) UnityEngine.Object.Destroy(h.group);
                    else { h.group.alpha = h.alpha; h.group.interactable = h.interactable; h.group.blocksRaycasts = h.raycasts; }
                    back++;
                }
                menuOff.Clear();
                menuHidden = false;
                McLog.Info($"freecam: {why} on the main menu: {back} part(s) shown again");
                return;
            }
            GameObject canvas = null;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == "Canvas" && root.GetComponent<Canvas>() != null) { canvas = root; break; }
            if (canvas == null) { McLog.Info($"freecam: {why} on the main menu: its canvas wasn't found"); return; }
            var hidden = new List<string>();
            var kept = new List<string>();
            foreach (Transform child in canvas.transform)
            {
                string name = child.gameObject.name;
                if (name.Contains("Logo") || name.EndsWith("Background")) { kept.Add(name); continue; }
                CanvasGroup g = child.GetComponent<CanvasGroup>();
                var h = new MenuHidden { group = g, added = g == null };
                if (g == null) h.group = g = child.gameObject.AddComponent<CanvasGroup>();
                else { h.alpha = g.alpha; h.interactable = g.interactable; h.raycasts = g.blocksRaycasts; }
                g.alpha = 0f;
                g.interactable = false;
                g.blocksRaycasts = false;
                menuOff.Add(h);
                hidden.Add(name);
            }
            menuHidden = true;
            McLog.Info($"freecam: {why} on the main menu: hidden {string.Join(", ", hidden)}; kept {string.Join(", ", kept)}");
        }

        /// <summary>Every frame. inLevel: Mycelium is in a level with a spore (the freecam turns off otherwise).</summary>
        public void Update(bool inLevel, bool mcScreenOpen)
        {
            if (!Allowed) return;
            if (!loggedAllowed)
            {
                loggedAllowed = true;
                McLog.Info("freecam: allowed (-freecam): '\\' switches it, I J K L move, O up and U down, the arrows turn, PageUp PageDown speed, Del hides the other spores, Insert gives yours new colours; those keys never go to Minecraft");
            }
            if (!hooked)
            {
                hooked = true;
                RenderPipelineManager.beginCameraRendering += BeforeDraw;
            }
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (!inLevel)
            {
                if (Active) SetActive(false, "left the level");
                if (OthersHidden) { OthersHidden = false; McLog.Info("freecam: left the level: the other spores shown again"); }
                return;
            }
            // A Minecraft screen open (chat): those keys are typing, not the freecam's.
            if (mcScreenOpen) return;
            if (kb.deleteKey.wasPressedThisFrame)
            {
                OthersHidden = !OthersHidden;
                McLog.Info($"freecam: Del: the other spores {(OthersHidden ? "hidden, their skills off" : "shown again, their skills back")}");
            }
            if (kb.backslashKey.wasPressedThisFrame) SetActive(!Active, "'\\'");
            if (kb.insertKey.wasPressedThisFrame) RandomColors("Insert");
            if (!Active) return;

            if (kb.pageUpKey.wasPressedThisFrame) SetSpeed(speed * 1.5f);
            if (kb.pageDownKey.wasPressedThisFrame) SetSpeed(speed / 1.5f);
            float turn = TurnSpeed * Time.unscaledDeltaTime;
            bool turned = false;
            if (kb.leftArrowKey.isPressed) { yaw -= turn; turned = true; }
            if (kb.rightArrowKey.isPressed) { yaw += turn; turned = true; }
            if (kb.upArrowKey.isPressed) { pitch -= turn; turned = true; }
            if (kb.downArrowKey.isPressed) { pitch += turn; turned = true; }
            Mouse mouse = Mouse.current;
            bool looking = mouse != null && mouse.rightButton.isPressed;
            if (looking != mouseLooking)
            {
                mouseLooking = looking;
                McLog.Info(looking ? "freecam: right button held: the mouse turns the freecam" : "freecam: right button let go: the mouse is Minecraft's again");
            }
            if (looking)
            {
                Vector2 d = mouse.delta.ReadValue();
                if (d.sqrMagnitude > 0f)
                {
                    yaw += d.x * MouseTurn;
                    pitch -= d.y * MouseTurn; // mouse up: look up
                    turned = true;
                }
            }
            HoldOffRightButtonActions(true);
            if (turned)
            {
                pitch = Mathf.Clamp(pitch, -89f, 89f);
                yaw = Mathf.Repeat(yaw, 360f);
                rotation = Quaternion.Euler(pitch, yaw, 0f);
            }
            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 move = Vector3.zero;
            if (kb.iKey.isPressed) move += forward;
            if (kb.kKey.isPressed) move -= forward;
            if (kb.lKey.isPressed) move += right;
            if (kb.jKey.isPressed) move -= right;
            if (kb.oKey.isPressed) move += Vector3.up;
            if (kb.uKey.isPressed) move -= Vector3.up;
            if (move.sqrMagnitude > 0f) position += move.normalized * speed * Time.unscaledDeltaTime;
            if (Time.unscaledTime >= nextLog && ((position - loggedFrom).sqrMagnitude > 0.01f || Mathf.Abs(Mathf.DeltaAngle(yaw, loggedYaw)) > 1f || Mathf.Abs(pitch - loggedPitch) > 1f))
            {
                nextLog = Time.unscaledTime + 1f;
                loggedFrom = position;
                loggedYaw = yaw;
                loggedPitch = pitch;
                McLog.Info($"freecam: at {position:F2}, facing {yaw:F0} degrees, looking {(pitch >= 0 ? "down" : "up")} {Mathf.Abs(pitch):F0}, speed {speed:F2} units/s");
            }
        }

        void SetActive(bool on, string why)
        {
            if (on)
            {
                Camera cam = Camera.main;
                if (cam == null) { McLog.Info("freecam: no main camera"); return; }
                position = cam.transform.position;
                rotation = cam.transform.rotation;
                Vector3 e = rotation.eulerAngles;
                yaw = e.y;
                pitch = e.x > 180f ? e.x - 360f : e.x; // Unity: positive x looks down
                pitch = Mathf.Clamp(pitch, -89f, 89f);
                rotation = Quaternion.Euler(pitch, yaw, 0f);
                loggedFrom = position;
            }
            Active = on;
            if (!on) { HoldOffRightButtonActions(false); mouseLooking = false; }
            McLog.Info(on ? $"freecam: on ({why}) at {position:F2} facing {rotation.eulerAngles.y:F0} degrees, speed {speed:F2} units/s"
                          : $"freecam: off ({why}); the camera follows you again");
        }

        void SetSpeed(float s)
        {
            speed = Mathf.Clamp(s, MinSpeed, MaxSpeed);
            McLog.Info($"freecam: speed {speed:F2} units/s");
        }

        void BeforeDraw(ScriptableRenderContext context, Camera cam)
        {
            if (!Active || cam != Camera.main) return;
            cam.transform.SetPositionAndRotation(position, rotation);
            LastDrawnAt = position;
            Drawn++;
        }

        /// <summary>Test aid: where the freecam last put the camera to draw, and how many frames.</summary>
        public Vector3 LastDrawnAt { get; private set; }
        public int Drawn { get; private set; }
        public void TestToggle() => SetActive(!Active, "test aid");
        public void TestRandomColors() => RandomColors("test aid");

        uint renameSeen;
        bool renameStarted;

        /// <summary>
        /// Minecraft's /changename (your request, 2026-11-12): McState's rename request, its number and
        /// the name. A new number renames the spore you play (CharacterStats.sporeName, then
        /// UpdateSporeName, which Mycelium calls itself: the HUD's name and the nametag). Only with
        /// -freecam. The first number seen is only remembered, so a request made before Mycelium
        /// started isn't done again. Minecraft sees it worked when MycState's spore name changes.
        /// Saved with the colony when Mycelium saves, as any spore's name.
        /// </summary>
        public void Rename(uint seq, string name)
        {
            if (!renameStarted) { renameStarted = true; renameSeen = seq; return; }
            if (seq == renameSeen) return;
            renameSeen = seq;
            name = (name ?? "").Trim();
            if (!Allowed) { McLog.Info($"rename: Minecraft asked to rename your spore to '{name}', but Mycelium wasn't started with -freecam"); return; }
            GameObject you = GameObject.FindWithTag("currentPlayer");
            System.Type statsType = System.Type.GetType("CharacterStats, Assembly-CSharp");
            Component stats = you != null && statsType != null ? you.GetComponent(statsType) : null;
            var field = statsType?.GetField("sporeName");
            if (stats == null || field == null || name.Length == 0) { McLog.Info($"rename: can't rename to '{name}' ({(name.Length == 0 ? "no name" : "no spore of yours")})"); return; }
            string was = field.GetValue(stats) as string;
            field.SetValue(stats, name);
            try { statsType.GetMethod("UpdateSporeName")?.Invoke(stats, null); }
            catch (Exception e) { McLog.Info($"rename: Mycelium's UpdateSporeName failed ({(e.InnerException ?? e).Message}); the name is set anyway"); }
            McLog.Info($"rename: your spore '{was}' is now '{field.GetValue(stats)}' (Minecraft's /changename, request {seq})");
        }

        static readonly string[] Species = { "Default", "Poison", "Coral", "Cordyceps" };
        static readonly string[] SpeciesFields = { "default", "poison", "coral", "cordyceps" };

        /// <summary>
        /// Insert (with -freecam, freecam on or off; your request, 2026-11-11): the spore you play gets
        /// new colours. One of the four species at random, then a cap colour and a body colour at random
        /// from that species' lists, the ones Mycelium itself picks from when a spore is born
        /// (SpawnCharacter's defaultCapColors / defaultBodyColors, poison..., coral..., cordyceps...;
        /// CreateSpeciesPalette). Set through its DesignTracker (SetCapColor, SetBodyColor), as Mycelium
        /// does. Only the colours: its species skill, eyes and mouth stay. Minecraft's skin follows by
        /// itself (ColonyWatch reads the colours every scan).
        /// </summary>
        void RandomColors(string why)
        {
            GameObject you = GameObject.FindWithTag("currentPlayer");
            System.Type spawnType = System.Type.GetType("SpawnCharacter, Assembly-CSharp");
            System.Type designType = System.Type.GetType("DesignTracker, Assembly-CSharp");
            var spawn = spawnType != null ? UnityEngine.Object.FindObjectOfType(spawnType, true) : null;
            Component design = you != null && designType != null ? you.GetComponent(designType) : null;
            if (spawn == null || design == null) { McLog.Info($"freecam: {why}: can't recolour (spore {(you != null ? you.name : "none")}, SpawnCharacter {(spawn != null ? "found" : "not found")}, DesignTracker {(design != null ? "found" : "not found")})"); return; }
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            int k = UnityEngine.Random.Range(0, Species.Length);
            var caps = spawnType.GetField(SpeciesFields[k] + "CapColors", F)?.GetValue(spawn) as List<Color>;
            var bodies = spawnType.GetField(SpeciesFields[k] + "BodyColors", F)?.GetValue(spawn) as List<Color>;
            if (caps == null || bodies == null || caps.Count == 0 || bodies.Count == 0) { McLog.Info($"freecam: {why}: {Species[k]} has no colour lists ({caps?.Count ?? -1} cap, {bodies?.Count ?? -1} body)"); return; }
            int c = UnityEngine.Random.Range(0, caps.Count), b = UnityEngine.Random.Range(0, bodies.Count);
            Color capWas = designType.GetField("capColor")?.GetValue(design) is Color cw ? cw : Color.clear;
            Color bodyWas = designType.GetField("bodyColor")?.GetValue(design) is Color bw ? bw : Color.clear;
            designType.GetMethod("SetCapColor")?.Invoke(design, new object[] { caps[c] });
            designType.GetMethod("SetBodyColor")?.Invoke(design, new object[] { bodies[b] });
            Color capNow = designType.GetField("capColor")?.GetValue(design) is Color cn ? cn : Color.clear;
            Color bodyNow = designType.GetField("bodyColor")?.GetValue(design) is Color bn ? bn : Color.clear;
            McLog.Info($"freecam: {why}: {you.name} recoloured as {Species[k]}: cap #{ColorUtility.ToHtmlStringRGB(capWas)} -> #{ColorUtility.ToHtmlStringRGB(capNow)} ({c + 1} of {caps.Count}), body #{ColorUtility.ToHtmlStringRGB(bodyWas)} -> #{ColorUtility.ToHtmlStringRGB(bodyNow)} ({b + 1} of {bodies.Count})");
        }
        public void TestToggleOthers() { OthersHidden = !OthersHidden; McLog.Info($"freecam: test aid: the other spores {(OthersHidden ? "hidden" : "shown")}"); }
        public void TestTurn(float degrees)
        {
            yaw = Mathf.Repeat(yaw + degrees, 360f);
            rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        public void TestFly(float seconds)
        {
            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;
            position += forward.normalized * speed * seconds;
        }

        /// <summary>
        /// While the freecam is on, Mycelium's own actions bound to the right mouse button (Interact by
        /// default) are off, so turning it doesn't also interact. Checked every 0.5 s (Mycelium or
        /// KeyOwnership may switch them back on); all put back when it goes off.
        /// </summary>
        void HoldOffRightButtonActions(bool on)
        {
            if (!on)
            {
                foreach (InputAction a in heldOff) if (a != null && !a.enabled) a.Enable();
                if (heldOff.Count > 0) McLog.Info($"freecam: {heldOff.Count} of Mycelium's right-button action(s) back on");
                heldOff.Clear();
                return;
            }
            if (Time.unscaledTime < nextHoldOff) return;
            nextHoldOff = Time.unscaledTime + 0.5f;
            var controller = ControllerType != null ? UnityEngine.Object.FindObjectOfType(ControllerType) : null;
            object actions = controller != null ? ActionsField?.GetValue(controller) : null;
            var asset = actions?.GetType().GetProperty("asset")?.GetValue(actions) as InputActionAsset;
            if (asset == null) return;
            foreach (InputActionMap map in asset.actionMaps)
                foreach (InputAction a in map.actions)
                {
                    if (!a.enabled) continue;
                    bool right = false;
                    foreach (InputBinding b in a.bindings) if (b.effectivePath == "<Mouse>/rightButton") right = true;
                    if (!right) continue;
                    a.Disable();
                    if (!heldOff.Contains(a)) { heldOff.Add(a); McLog.Info($"freecam: Mycelium's '{a.name}' (on the right button) off while the freecam is on"); }
                }
        }

        /// <summary>The input bridge's "not sent to Minecraft" keys, with the freecam's added when allowed.</summary>
        public HashSet<int> Keys(HashSet<int> owned)
        {
            if (!Allowed) return owned;
            keys.Clear();
            keys.UnionWith(owned);
            keys.UnionWith(Scancodes);
            return keys;
        }

        readonly HashSet<int> keys = new HashSet<int>();
    }
}
