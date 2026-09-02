using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using BlockBlast.Core;

/// <summary>
/// The gear menu: sound, music and haptics, plus a restart.
///
/// Only what this game actually has. The reference's panel also offers More Games, More
/// Settings and Home, all of which belong to a shell this build does not have -- a single
/// classic mode with no store and no menu. Shipping those as dead buttons would be worse
/// than leaving them out.
///
/// The board keeps running behind the panel rather than pausing. There is no timer and no
/// falling piece, so a pause would only add a state to get wrong.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    [Header("Colours")]
    [SerializeField] private Color panelColor = new Color32(16, 17, 28, 250);
    [SerializeField] private Color panelRimColor = new Color32(120, 245, 255, 255);
    [SerializeField] private Color buttonColor = new Color32(0, 190, 215, 255);
    [SerializeField] private Color backdropColor = new Color(0.02f, 0.02f, 0.05f, 0.80f);

    [SerializeField] private float fadeDuration = 0.18f;

    private BlockSkin skin;
    private GameManager gameManager;

    private Button gearButton;
    private CanvasGroup group;
    private Image soundSlash;
    private Image hapticsSlash;
    private Image glowSlash;

    private Coroutine transition;

    public bool IsOpen => group != null && group.gameObject.activeSelf;

    private void Awake()
    {
        gameManager = FindAnyObjectByType<GameManager>();

        GridVisualizer visualizer = FindAnyObjectByType<GridVisualizer>();
        skin = visualizer != null ? visualizer.Skin : null;
        if (skin == null) skin = Resources.Load<BlockSkin>(BlockSkin.ResourcesPath);

        Build();
    }

    private void Build()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[SettingsPanel] No Canvas in the scene; settings cannot be built.", this);
            return;
        }

        // The gear lives on the HUD, not inside the panel, so it stays reachable.
        gearButton = UIFactory.IconButton(canvas.transform, "GearButton",
            skin != null ? skin.GearIcon : null, new Color32(120, 245, 255, 255),
            72f, new Vector2(1f, 1f), new Vector2(-56f, -60f));
        gearButton.onClick.AddListener(Open);

        RectTransform root = UIFactory.Stretch(canvas.transform, "SettingsPanel");
        root.SetAsLastSibling();

        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        // Tapping the dimmed area closes, which is what players try first.
        Image backdrop = UIFactory.Backdrop(root, backdropColor);
        var backdropButton = backdrop.gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(Close);

        var panelSize = new Vector2(760f, 700f);
        RectTransform panel = UIFactory.Panel(root, "Panel", panelSize, Vector2.zero,
            skin != null ? skin.PanelSprite : null, skin != null ? skin.PanelRimSprite : null,
            panelColor, panelRimColor);

        UIFactory.Text(panel, "Title", "Settings", 56f, Color.white,
            new Vector2(panelSize.x, 90f), new Vector2(0f, panelSize.y * 0.5f - 78f));

        Button close = UIFactory.IconButton(panel, "Close",
            skin != null ? skin.CloseIcon : null, panelRimColor,
            54f, new Vector2(1f, 1f), new Vector2(-34f, -34f));
        close.onClick.AddListener(Close);

        // Toggles across the top, buttons below, matching the reference's grouping.
        float toggleY = panelSize.y * 0.5f - 250f;
        var toggleSize = new Vector2(168f, 190f);

        // Three toggles, not four: the music one went with the soundtrack. A switch that
        // controls nothing is worse than no switch, because the player who flips it and
        // hears no difference has learned the settings cannot be trusted.
        Button sound = UIFactory.IconToggle(panel, "SoundToggle", "Sound",
            skin != null ? skin.SoundIcon : null, skin != null ? skin.SlashIcon : null,
            Color.white, toggleSize, new Vector2(-172f, toggleY), 30f, out soundSlash);
        sound.onClick.AddListener(() => { GameSettings.Sound = !GameSettings.Sound; Refresh(); });

        Button haptics = UIFactory.IconToggle(panel, "HapticsToggle", "Vibrate",
            skin != null ? skin.VibrateIcon : null, skin != null ? skin.SlashIcon : null,
            Color.white, toggleSize, new Vector2(0f, toggleY), 30f, out hapticsSlash);
        haptics.onClick.AddListener(() =>
        {
            GameSettings.Haptics = !GameSettings.Haptics;
            // Buzz when switched on. It is the only way a player -- or anyone without the
            // phone in front of them -- can tell a working device from a broken code path
            // from a preference that was off the whole time.
            if (GameSettings.Haptics) Haptics.Test();
            Refresh();
        });

        Button glow = UIFactory.IconToggle(panel, "GlowToggle", "Glow",
            skin != null ? skin.Star : null, skin != null ? skin.SlashIcon : null,
            Color.white, toggleSize, new Vector2(172f, toggleY), 30f, out glowSlash);
        glow.onClick.AddListener(() =>
        {
            GameSettings.Glow = !GameSettings.Glow;
            var postFx = FindAnyObjectByType<NeonPostFx>();
            if (postFx != null) postFx.Refresh();
            Refresh();
        });

        Button restart = UIFactory.PillButton(panel, "RestartButton", "Restart",
            skin != null ? skin.ButtonSprite : null, skin != null ? skin.ReplayIcon : null,
            buttonColor, Color.white, new Vector2(600f, 150f), new Vector2(0f, -150f), 52f);
        restart.onClick.AddListener(Restart);

        root.gameObject.SetActive(false);
    }

    /// <summary>A struck-through icon reads as off; a plain one as on.</summary>
    private void Refresh()
    {
        if (soundSlash != null) soundSlash.enabled = !GameSettings.Sound;
        if (hapticsSlash != null) hapticsSlash.enabled = !GameSettings.Haptics;
        if (glowSlash != null) glowSlash.enabled = !GameSettings.Glow;
    }

    public void Open()
    {
        if (group == null || IsOpen) return;

        Refresh();
        group.gameObject.SetActive(true);
        group.blocksRaycasts = true;

        if (transition != null) StopCoroutine(transition);
        transition = StartCoroutine(UIFactory.Fade(group, 0f, 1f, fadeDuration));
    }

    public void Close()
    {
        if (group == null || !IsOpen) return;

        group.blocksRaycasts = false;
        if (transition != null) StopCoroutine(transition);
        transition = StartCoroutine(CloseRoutine());
    }

    private IEnumerator CloseRoutine()
    {
        yield return UIFactory.Fade(group, group.alpha, 0f, fadeDuration);
        group.gameObject.SetActive(false);
        transition = null;
    }

    private void Restart()
    {
        Close();
        if (gameManager != null) gameManager.RestartGame();
    }

    private void Start()
    {
        // Apply the stored preference at launch, not only when the panel is opened.
        Refresh();
    }

    private void OnEnable()
    {
        if (gameManager != null) gameManager.OnGameStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        if (gameManager != null) gameManager.OnGameStateChanged -= HandleGameStateChanged;
    }

    /// <summary>
    /// The gear has nothing to offer once the run has ended, and the end screen covers
    /// the whole display, so leaving it visible only invites a tap that does nothing.
    /// </summary>
    private void HandleGameStateChanged(bool isGameOver)
    {
        if (isGameOver) Close();
        if (gearButton != null) gearButton.gameObject.SetActive(!isGameOver);
    }
}
