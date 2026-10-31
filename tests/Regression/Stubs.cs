using System.Collections;
using System.Text.RegularExpressions;

namespace UnityEngine
{
    public class Component
    {
        public GameObject gameObject { get; internal set; }
    }

    public class Transform : Component
    {
        private readonly List<Transform> _children = new();
        public string name => gameObject.name;
        public Transform parent { get; private set; }
        public int childCount => _children.Count;

        public void Add(Transform child)
        {
            child.parent = this;
            _children.Add(child);
        }

        public Transform GetChild(int index) => _children[index];
        public int GetSiblingIndex() => parent == null ? 0 : parent._children.IndexOf(this);
        public bool TryGetComponent<T>(out T component) where T : Component => gameObject.TryGetComponent(out component);
        public T GetComponent<T>() where T : Component
            => gameObject.TryGetComponent(out T component) ? component : null;

        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var result = new List<T>();
            void Visit(Transform current)
            {
                if (!includeInactive && !current.gameObject.activeInHierarchy) return;
                if (current is T self) result.Add(self);
                foreach (var child in current._children) Visit(child);
            }
            Visit(this);
            return result.ToArray();
        }

        public Transform Find(string path)
        {
            var current = this;
            foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                current = current?._children.FirstOrDefault(child => child.name == part);
                if (current == null) return null;
            }
            return current;
        }

        public string FullPath => parent == null ? name : parent.FullPath + "/" + name;
    }

    public class GameObject
    {
        private static readonly Dictionary<string, GameObject> Roots = new();
        private readonly Dictionary<Type, Component> _components = new();
        public string name { get; }
        public Transform transform { get; }
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);

        public GameObject(string name)
        {
            this.name = name;
            transform = new Transform { gameObject = this };
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            _components[typeof(T)] = component;
            return component;
        }

        public bool TryGetComponent<T>(out T component) where T : Component
        {
            component = _components.TryGetValue(typeof(T), out var value) ? (T)value : null;
            return component != null;
        }

        public T GetComponent<T>() where T : Component =>
            TryGetComponent(out T component) ? component : null;

        public static Action BeforeFind;
        public static GameObject Find(string name)
        {
            BeforeFind?.Invoke();
            return Roots.TryGetValue(name, out var root) ? root : null;
        }

        public static GameObject Resolve(string path)
        {
            var parts = (path ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !Roots.TryGetValue(parts[0], out var root)) return null;
            return parts.Length == 1 ? root : root.transform.Find(string.Join("/", parts.Skip(1)))?.gameObject;
        }

        public static GameObject CreatePath(string path)
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (!Roots.TryGetValue(parts[0], out var root))
            {
                root = new GameObject(parts[0]);
                Roots.Add(parts[0], root);
            }
            var parent = root.transform;
            foreach (var part in parts.Skip(1))
            {
                var child = parent.Find(part);
                if (child == null)
                {
                    child = new GameObject(part).transform;
                    parent.Add(child);
                }
                parent = child;
            }
            return parent.gameObject;
        }

        public static void Clear() => Roots.Clear();
    }

    public class WaitForSeconds
    {
        public WaitForSeconds(float seconds) { }
    }

    public static class Time
    {
        public static float unscaledTime;
    }

    public enum KeyCode { None, F1, F2, F6, F7, F8, F9 }
    public static class Input
    {
        public static KeyCode Pressed;
        public static bool GetKeyDown(KeyCode key) => key != KeyCode.None && Pressed == key;
    }
    public static class Mathf
    {
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    }
}

namespace UnityEngine.UI
{
    public class Button : UnityEngine.Component
    {
        public bool enabled = true;
        public bool interactable = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
        public readonly ClickEvent onClick = new();

        public class ClickEvent
        {
            public Action Action;
            public void Invoke() => Action?.Invoke();
        }
    }
}

namespace UnityEngine.EventSystems
{
    public class EventSystem
    {
        public static EventSystem current = new();
    }

    public class PointerEventData
    {
        public enum InputButton { Left }
        public PointerEventData(EventSystem system) { }
        public InputButton button;
    }

    public static class ExecuteEvents
    {
        public static readonly List<string> Events = new();
        public static readonly List<string> Targets = new();
        public static Action<UnityEngine.GameObject> OnDown;
        public static readonly Action<UnityEngine.GameObject, PointerEventData> pointerDownHandler =
            (target, _) =>
            {
                Events.Add("down");
                Targets.Add(target.transform.FullPath);
                OnDown?.Invoke(target);
            };
        public static readonly Action<UnityEngine.GameObject, PointerEventData> pointerUpHandler =
            (_, _) => Events.Add("up");
        public static readonly Action<UnityEngine.GameObject, PointerEventData> pointerClickHandler =
            (_, _) => Events.Add("click");

        public static void Execute(UnityEngine.GameObject target, PointerEventData data,
            Action<UnityEngine.GameObject, PointerEventData> handler) => handler(target, data);
    }
}

namespace Il2Cpp
{
    public sealed class ObscuredBool
    {
        public bool Value;
        public bool GetDecrypted() => Value;
    }

    public sealed class Guardian
    {
        public string guardianName;
        public ObscuredBool IsUnlocked = new();
    }

    public sealed class GuardianInteractionSquare : UnityEngine.Component
    {
        public bool isEmpty;
        public Guardian guardian;
    }
}

namespace MelonLoader
{
    public class MelonInfoAttribute : Attribute
    {
        public MelonInfoAttribute(Type type, string name, string version, string author) { }
    }
    public class MelonGameAttribute : Attribute { }
    public class MelonColorAttribute : Attribute { public MelonColorAttribute(int a, int r, int g, int b) { } }
    public class MelonAuthorColorAttribute : Attribute { public MelonAuthorColorAttribute(int a, int r, int g, int b) { } }
    public class MelonMod
    {
        public class Log
        {
            public void Msg(string message) { }
            public void Warning(string message) { }
        }
        public Log LoggerInstance = new();
        public virtual void OnInitializeMelon() { }
        public virtual void OnApplicationQuit() { }
        public virtual void OnSceneWasLoaded(int buildIndex, string sceneName) { }
        public virtual void OnSceneWasUnloaded(int buildIndex, string sceneName) { }
        public virtual void OnUpdate() { }
    }

    public class MelonPreferences_Entry
    {
        public string Identifier;
        public string DisplayName;
        public string Description;
        public bool IsHidden;
        public virtual object BoxedValue => null;
    }

    public class MelonPreferences_Entry<T> : MelonPreferences_Entry
    {
        public T Value { get; set; }
        public override object BoxedValue => Value;
    }

    public class MelonPreferences_Category
    {
        public string Identifier;
        public string DisplayName;
        public int SaveCalls;
        public bool FailSave;
        public readonly Dictionary<string, MelonPreferences_Entry> Entries = new();

        public MelonPreferences_Entry<T> CreateEntry<T>(string id, T value, string display, string description,
            bool is_hidden = false)
        {
            var entry = new MelonPreferences_Entry<T>
            {
                Identifier = id, DisplayName = display, Description = description,
                Value = MelonPreferences.SavedValues.TryGetValue(Identifier + "." + id, out var saved)
                    ? (T)saved : value,
                IsHidden = is_hidden
            };
            Entries[id] = entry;
            return entry;
        }

        public void SetFilePath(string path) { }
        public void SaveToFile()
        {
            SaveCalls++;
            if (FailSave) throw new IOException("Preference save failed.");
        }
    }

    public static class MelonPreferences
    {
        public static readonly Dictionary<string, MelonPreferences_Category> Categories = new();
        public static readonly Dictionary<string, object> SavedValues = new();
        public static MelonPreferences_Category CreateCategory(string id, string display)
        {
            var category = new MelonPreferences_Category { Identifier = id, DisplayName = display };
            Categories[id] = category;
            return category;
        }
        public static void Save() { }
    }

    public static class MelonCoroutines
    {
        public static readonly Dictionary<object, IEnumerator> Active = new();
        public static int Starts;
        public static object Start(IEnumerator routine)
        {
            var handle = new object();
            Active[handle] = routine;
            Starts++;
            return handle;
        }
        public static void Stop(object handle)
        {
            if (Active.Remove(handle, out var routine)) (routine as IDisposable)?.Dispose();
        }
    }
}

namespace MelonLoader.Utils
{
    public static class MelonEnvironment
    {
        public static string MelonBaseDirectory => Environment.CurrentDirectory;
    }
}

namespace Firebot.Core
{
    public static class Logger
    {
        public static readonly List<string> Messages = new();
        public static void Debug(string message) => Messages.Add(message);
        public static void Info(string message) => Messages.Add(message);
        public static void Warning(string message) => Messages.Add(message);
        public static void Error(string message) => Messages.Add(message);
    }

    public static class LevelRequirements { public const int None = 0; }
    public static class PlayerStats
    {
        public static bool TryGetCharacterLevel(out int level) { level = 9999; return true; }
    }
    public static class BotManager
    {
        public static bool IsRunning;
        public static void Initialize() { }
        public static void Start() => IsRunning = true;
        public static void Stop() => IsRunning = false;
        public static void WatchForStall() { }
        public static bool ShouldPauseAutoUpgrade() => false;
        public static string AutoUpgradePauseReason => "";
    }
    public static class HotkeyGate
    {
        public static bool IsCapturing;
        public static void BeginFrame(bool capturing) => IsCapturing = capturing;
    }
    public static class ScreenMap { public static void Tick() { } }
    public static class DropCollector { public static void Tick() { } }
    public static class BubbleWatcher { public static void Tick() { } }
    public static class Watchdog
    {
        public static IEnumerator ForceClearAll(string phase = null) { yield break; }
    }
}

namespace Firebot.UI
{
    public static class BotPanel
    {
        public static void Initialize() { }
        public static void OnSceneChanged() { }
        public static void Tick() { }
    }
    public static class BotStatusScreen
    {
        public static UnityEngine.KeyCode Key => UnityEngine.KeyCode.F2;
        public static void Initialize() { }
        public static void OnSceneChanged() { }
        public static void Tick() { }
    }
}
namespace Firebot.UI.Widgets { public static class KeybindRow { public static bool IsListening; } }
namespace Firebot.Planner
{
    public static class SquadApplier
    {
        public static int Active = 1;
        public static readonly HashSet<int> Populated = new() { 1, 2, 3 };
        public static readonly List<int> Requests = new();
        public static Action<int> OnSet;
        public static int ActiveSlot() => Active;
        public static bool HasMachines(int slot) => Populated.Contains(slot);
        public static IEnumerator SetActive(int slot, string why)
        {
            Requests.Add(slot);
            if (OnSet != null) OnSet(slot);
            else Active = slot;
            yield break;
        }
        public static void Reset()
        {
            Active = 1;
            Populated.Clear();
            Populated.UnionWith(new[] { 1, 2, 3 });
            Requests.Clear();
            OnSet = null;
        }
    }

    public static class PlannerServer
    {
        public static void Start() { }
        public static void Stop() { }
        public static void Tick() { }
    }
}

namespace Firebot.Utilities
{
    public static class StringUtils
    {
        public static string Humanize(string text) =>
            Regex.Replace(text.Replace("Task", ""), "(?<!^)([A-Z])", " $1");
    }
}

namespace Firebot.UI.Config
{
    internal static class ConfigRegistry
    {
        public static void LogUnwritable(MelonLoader.MelonPreferences_Category category,
            MelonLoader.MelonPreferences_Entry entry) { }
        public static void MarkDirty(MelonLoader.MelonPreferences_Category category) { }
    }
}

namespace Firebot.GameModel.Base
{
    public class GameElement
    {
        protected string Path { get; }
        protected UnityEngine.Transform Root => UnityEngine.GameObject.Resolve(Path)?.transform;
        public string Name => Root?.name ?? "";

        public GameElement(string path = null, GameElement parent = null, UnityEngine.Transform transform = null)
        {
            Path = transform?.FullPath ?? (parent == null ? path : parent.Path + "/" + (path ?? "").Trim('/'));
        }

        public virtual bool IsVisible() => Root != null && Root.gameObject.activeInHierarchy;
        public IEnumerable<GameElement> GetChildren()
        {
            if (Root == null) yield break;
            for (var i = 0; i < Root.childCount; i++) yield return new GameElement(transform: Root.GetChild(i));
        }
        public GameElement GetChild(int index)
            => Root != null && index >= 0 && index < Root.childCount
                ? new GameElement(transform: Root.GetChild(index)) : null;
        protected void Debug(string message) { }
    }
}

namespace Firebot.GameModel.Primitives
{
    using Firebot.GameModel.Base;
    using UnityEngine;
    using UnityEngine.UI;

    public class GameButton : GameElement
    {
        public GameButton(string path = null, GameElement parent = null, Transform transform = null)
            : base(path, parent, transform) { }

        public bool IsClickable() => IsVisible() && Root.TryGetComponent(out Button button) &&
                                     button.enabled && button.interactable;
        public virtual IEnumerator Click()
        {
            if (IsClickable() && Root.TryGetComponent(out Button button)) button.onClick.Invoke();
            yield return null;
        }
        public IEnumerator ClickUntil(Func<bool> stateChanged, Action<bool> result, string label, float timeout = 2f)
        {
            if (!IsClickable()) { result(false); yield break; }
            yield return Click();
            result(stateChanged());
        }
    }

    public class GameText : GameElement
    {
        public static readonly Dictionary<string, DateTime> Times = new();
        public static readonly Dictionary<string, string> Texts = new();
        public GameText(string path = null, GameElement parent = null) : base(path, parent) { }
        public DateTime Time => Times.TryGetValue(Path, out var value) ? value : DateTime.MinValue;
        public string GetParsedText() => Texts.TryGetValue(Path, out var value) ? value : "";
    }
}

namespace Firebot.Infrastructure
{
    public static class Paths
    {
        public static class BattleLoc
        {
            public static class BottomSideUIDesktopLoc
            {
                public static class MenuButtonsLoc { public const string UpgradesBtn = "battle/upgrades"; }
                public static class LeaderPanelLoc
                {
                    public const string HotKeyOneBtn = "battle/leader/one";
                    public const string HotKeyTwoBtn = "battle/leader/two";
                    public const string HotKeyThreeBtn = "battle/leader/three";
                }
            }
            public static class NotificationsLoc
            {
                public const string MapMissionsBtn = "notifications/MapMissions";
                public const string FirestoneResearchBtn = "notifications/FirestoneResearch";
            }
        }
        public static class MenusLoc
        {
            public static class CanvasLoc
            {
                public static class MainSceneLoc
                {
                    public static class UpgradesLoc
                    {
                        public const string UpgradesList = "menus/Upgrades/upgradesList";
                        public const string BuyBtn = "/buyButton";
                        public const string LvlUpBtn = "/lvlUpButton";
                    }
                }
                public static class MapLoc
                {
                    public static class MissionsLoc
                    {
                        public static class PinLoc { public const string Root = "mapPins"; }
                    }
                }
                public static class TownLoc
                {
                    public static class LibraryLoc
                    {
                        public static class ResearchPanelLoc
                        {
                            public const string Root = "menus/Library/submenus/firestoneResearch/researchPanel";
                            public const string SelectResearchTable = Root + "/selectResearchTable";
                            public const string ClaimBtn = "/container/claimButton";
                            public const string NextRunTimeTxt = "/container/researchInfo/progressBarBg/timeLeftText";
                            public const string SpeedupBtn = "/container/speedUpButton";
                            public const string SpeedupFinishDesc = SpeedupBtn + "/finishDesc";
                        }
                        public static class NodeLoc
                        {
                            public const string Root = "menus/Library/submenus/firestoneResearch/researchScrollView/viewport/content/submenus";
                            public const string Glow = "/glow";
                            public const string ProgressBar = "/progressBarBg";
                            public const string CompletedTxt = "/genericText";
                        }
                        public static class PreviewLoc
                        {
                            public const string Root = "popups/FirestoneResearchPreview";
                            public const string UnlockedTxt = Root + "/bg/unlocked";
                            public const string MaxedTxt = Root + "/bg/maxed";
                            public const string ActivateBtn = Root + "/bg/unlocked/buttonHolder/researchActivateButton";
                        }
                    }
                }
            }
        }
    }
}

namespace Firebot.Behaviors.Extra
{
    using UnityEngine;

    internal static class P
    {
        public const string Menus = "menus";
        public const string Notifications = "notifications";
        public const string Popups = "popups";
        public const string Battle = "battle";
    }

    internal static class Ui
    {
        public static bool OpenMapWorks = true;
        public static bool OpenTownWorks = true;
        public static int OpenMapCalls;
        public static Action<string> BeforeGuardedClick;
        public static readonly Dictionary<string, string> Texts = new();
        public static readonly Dictionary<string, double> Numbers = new();
        public static bool Visible(string path) => GameObject.Resolve(path)?.activeInHierarchy ?? false;
        public static string Text(string path) => path.EndsWith("/squads/quantity") ? MapFixture.Free.ToString()
            : Texts.TryGetValue(path, out var value) ? value : "";
        public static string Text(Transform target) =>
            target != null && target.gameObject.activeInHierarchy ? Text(target.FullPath) : "";
        public static double NumberAt(string path) => path.EndsWith("/squads/quantity") ? MapFixture.Free
            : Numbers.TryGetValue(path, out var value) ? value : -1;
        public static Transform Find(string path) => GameObject.Resolve(path)?.transform;
        public static IEnumerable<Transform> VisibleChildren(string path)
        {
            var root = Find(path);
            if (root == null) yield break;
            for (var i = 0; i < root.childCount; i++)
                if (root.GetChild(i).gameObject.activeInHierarchy) yield return root.GetChild(i);
        }
        public static bool Clickable(string path) => Clickable(Find(path));
        public static bool Clickable(Transform target) => target != null && target.gameObject.activeInHierarchy &&
            target.TryGetComponent(out UnityEngine.UI.Button button) && button.enabled && button.interactable;
        public static IEnumerator ClickUntil(Transform target, Func<bool> changed, Action<bool> completed,
            string label, float wait = 1f, float timeout = 2f)
        {
            if (!Clickable(target)) { completed(false); yield break; }
            target.TryGetComponent(out UnityEngine.UI.Button button);
            button.onClick.Invoke();
            completed(changed());
            yield break;
        }
        public static IEnumerator WaitFor(string path, float timeout = 5f) { yield break; }
        public static IEnumerator WaitUntil(Func<bool> condition, Action<bool> completed, float timeout = 2f)
        {
            completed(condition());
            yield break;
        }
        public static IEnumerator OpenMapSubmenu(string name)
        {
            OpenMapCalls++;
            if (OpenMapWorks) GameObject.CreatePath("menus/WorldMap/submenus/mapMissionsSubmenu").activeSelf = true;
            yield break;
        }
        public static IEnumerator OpenTownSubmenu(string building, string path, string tab)
        {
            if (OpenTownWorks) GameObject.CreatePath(path).activeSelf = true;
            yield break;
        }
        public static IEnumerator OpenTownBuilding(string building)
        {
            if (OpenTownWorks && building == "templeOfEternals")
                GameObject.CreatePath("menus/TempleOfEternals").activeSelf = true;
            yield break;
        }
        public static IEnumerator Click(string path, float wait = -1)
            => Click(Find(path), wait);
        public static IEnumerator Click(Transform target, float wait = -1, string label = null)
        {
            if (Clickable(target))
            {
                target.TryGetComponent(out UnityEngine.UI.Button button);
                button.onClick.Invoke();
            }
            yield break;
        }
        public static IEnumerator ClickIf(string path, Func<bool> condition, Action<bool> clicked, float wait = -1,
            string label = null)
        {
            var target = Find(path);
            return ClickIf(target, () => target == Find(path) && condition(), clicked, wait, label);
        }
        public static IEnumerator ClickIf(Transform target, Func<bool> condition, Action<bool> clicked, float wait = -1,
            string label = null)
        {
            BeforeGuardedClick?.Invoke(target?.FullPath);
            if (target != null && Clickable(target) && condition())
            {
                try
                {
                    target.TryGetComponent(out UnityEngine.UI.Button button);
                    button.onClick.Invoke();
                    clicked(true);
                }
                catch { clicked(false); }
            }
            else clicked(false);
            yield break;
        }
    }

    internal static class MapFixture
    {
        public sealed class Mission
        {
            public bool Active;
            public bool Completed = false;
            public DateTime Ends;
        }
        public static readonly Dictionary<string, Mission> Missions = new();
        public static double Free;
        public static string Selected;
        public static int Starts;
        public static string StartMode = "success";
    }
}

namespace Firebot.GameModel.Shared
{
    public static class Notifications
    {
        private static IEnumerator Nothing() { yield break; }
        public static IEnumerator MapMissions => Nothing();
        public static IEnumerator FirestoneResearch => Nothing();
    }
}

namespace Firebot.GameModel.Features.Map
{
    public static class MapMission
    {
        public static DateTime NextRunTime;
        public static IEnumerator Close { get { yield break; } }
    }
}

namespace Firebot.GameModel.Features.MainScene
{
    public static class Upgrades
    {
        public static bool IsVisible = true;
        public static IEnumerator Close { get { yield break; } }
        public static IEnumerator SetUpgradeLevel(string mode) { yield break; }
    }
}

namespace Firebot.GameModel.Features.Map.Missions
{
    using Firebot.Behaviors.Extra;
    using Firebot.GameModel.Base;
    using Firebot.GameModel.Primitives;

    public class MissionPin : GameElement
    {
        public MissionPin(GameElement parent) : base(parent: parent) { }
        private MapFixture.Mission Mission => MapFixture.Missions[Name];
        public bool IsActive => Mission.Active;
        public bool IsCompleted => Mission.Completed;
        public DateTime TimeRequired => Mission.Ends;
        public IEnumerator Select() { MapFixture.Selected = Name; yield break; }
    }

    public static class MissionPreview
    {
        public static GameButton SpeedupBtn => new("missing");
        public static bool CanSpeedup => false;
        public static bool IsNotEnoughSquads => false;
        public static DateTime NextRunTime => DateTime.MinValue;
        public static IEnumerator Close { get { yield break; } }
        public static IEnumerator StartMission
        {
            get
            {
                MapFixture.Starts++;
                if (MapFixture.StartMode == "success")
                {
                    MapFixture.Missions[MapFixture.Selected].Active = true;
                    MapFixture.Free--;
                }
                else if (MapFixture.StartMode == "unreadable") MapFixture.Free = -1;
                yield break;
            }
        }
    }

    public static class MissionRewardsPopup { public static IEnumerator Close { get { yield break; } } }
}
