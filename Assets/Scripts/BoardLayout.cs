using UnityEngine;
using BlockBlast.Core;

/// <summary>
/// Applies <see cref="ScreenLayout"/> to the scene: sizes the camera, and re-fits
/// everything when the screen changes.
///
/// This component is optional. <see cref="GridManager"/> and <see cref="SpawnManager"/>
/// each ask <see cref="ScreenLayout"/> for their own position while initializing, so the
/// layout is already correct without it -- Unity does not define an Awake order between
/// them, so pushing positions from a single component could not be made reliable. What
/// this adds is the camera size and reacting to a rotation or a window resize.
/// </summary>
[DefaultExecutionOrder(-200)]
public class BoardLayout : MonoBehaviour
{
    [SerializeField] private LayoutConfig layout = LayoutConfig.Default;

    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private SpawnManager spawnManager;

    private int lastWidth;
    private int lastHeight;

    /// <summary>
    /// The proportions every component uses. Static so the grid and tray can lay
    /// themselves out whether or not this component is in the scene.
    /// </summary>
    public static LayoutConfig Active { get; private set; } = LayoutConfig.Default;

    private void Awake()
    {
        Active = layout.Sanitized();
        Resolve();
        Apply();
    }

    private void Update()
    {
        // Orientation changes and desktop window resizes both land here. There is no
        // dependable resize callback in a built player, and this comparison is free.
        if (Screen.width == lastWidth && Screen.height == lastHeight) return;
        Apply();
    }

    private BlockSkin cachedSkin;

    private BlockSkin ResolveSkin()
    {
        if (cachedSkin != null) return cachedSkin;

        GridVisualizer visualizer = FindAnyObjectByType<GridVisualizer>();
        cachedSkin = visualizer != null && visualizer.Skin != null
            ? visualizer.Skin
            : Resources.Load<BlockSkin>(BlockSkin.ResourcesPath);   // already order-safe

        return cachedSkin;
    }

    private void Resolve()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (gridManager == null) gridManager = FindAnyObjectByType<GridManager>();
        if (spawnManager == null) spawnManager = FindAnyObjectByType<SpawnManager>();
    }

    private void Apply()
    {
        Resolve();
        if (targetCamera == null || gridManager == null) return;

        lastWidth = Screen.width;
        lastHeight = Screen.height;

        GridGeometry geometry = gridManager.Geometry;
        ScreenLayout fit = ScreenLayout.ForCurrentScreen(geometry, Active);

        targetCamera.orthographic = true;
        targetCamera.orthographicSize = fit.OrthographicSize;

        // The page colour is part of the art, not a scene setting -- leaving Unity's
        // default grey behind a board that was authored against deep blue makes every
        // other colour read wrong.
        //
        // Assigned as-is. The project renders in linear space, but the clear colour is
        // written straight to the framebuffer, so the sRGB value is what lands on screen:
        // measured against the reference this reproduces it exactly, while converting to
        // linear first renders it far too dark.
        BlockSkin skin = ResolveSkin();
        if (skin != null)
        {
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = skin.PageBackgroundColor;
        }

        gridManager.SetCenter(fit.BoardCenter);

        if (spawnManager != null)
        {
            spawnManager.SetTrayLayout(
                new Vector3(fit.TrayCenter.x, fit.TrayCenter.y, 0f),
                fit.SlotSpacing(spawnManager.SlotCount));
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        Active = layout.Sanitized();
        if (Application.isPlaying) Apply();
    }
#endif
}
