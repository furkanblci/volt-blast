using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the game's UI in code.
///
/// The panels are assembled here rather than authored in the scene for the same reason
/// the board is: they have to size themselves to whatever screen they land on, and an
/// authored hierarchy silently stops matching the moment a value it was laid out against
/// changes. It also keeps the whole look in one reviewable place instead of spread across
/// Inspector fields nobody can diff.
///
/// Every helper returns the RectTransform so callers can position without re-fetching it.
/// </summary>
public static class UIFactory
{
    /// <summary>Full-rect child that fills its parent. The base for panels and backdrops.</summary>
    public static RectTransform Stretch(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    /// <summary>A fixed-size child anchored to its parent's centre.</summary>
    public static RectTransform Box(Transform parent, string name, Vector2 size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
        return rt;
    }

    /// <summary>
    /// A dialog body with its neon edge on top. Two images rather than one because the
    /// body is a neutral dark and the rim is a saturated accent; a single tinted sprite
    /// can only be one hue.
    /// </summary>
    public static RectTransform Panel(
        Transform parent, string name, Vector2 size, Vector2 position,
        Sprite body, Sprite rim, Color bodyColor, Color rimColor)
    {
        RectTransform rect = Box(parent, name, size, position);

        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = body;
        image.color = bodyColor;
        image.type = Image.Type.Sliced;
        image.raycastTarget = true;   // stop taps falling through to whatever is behind

        if (rim != null)
        {
            RectTransform rimRect = Stretch(rect, "Rim");
            Sprite(rimRect, rim, rimColor, sliced: true);
        }

        return rect;
    }

    public static Image Sprite(RectTransform rect, Sprite sprite, Color color, bool sliced = false)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>Dimmed, tap-absorbing backdrop. Blocks input to whatever is behind it.</summary>
    public static Image Backdrop(Transform parent, Color color)
    {
        RectTransform rect = Stretch(parent, "Backdrop");
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;

        // Deliberately raycast-blocking: a modal that lets taps through to the board is
        // how a player accidentally places a piece while reading a dialog.
        image.raycastTarget = true;
        return image;
    }

    public static TextMeshProUGUI Text(
        Transform parent, string name, string content, float size, Color color,
        Vector2 boxSize, Vector2 position, FontStyles style = FontStyles.Bold)
    {
        RectTransform rect = Box(parent, name, boxSize, position);

        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>
    /// A pill button with an optional icon on the left. Returns the Button so the caller
    /// wires the click; the label and icon are parented inside and need no further care.
    /// </summary>
    /// <summary>
    /// Raised whenever a button built here is pressed.
    ///
    /// An event rather than a direct call into audio: this factory has no business
    /// knowing what a click causes, and every screen's buttons are built here, so one
    /// subscription covers all of them without each screen remembering to wire a sound.
    /// </summary>
    public static event Action ButtonPressed;

    private static void AnnouncePress() => ButtonPressed?.Invoke();

    public static Button PillButton(
        Transform parent, string name, string label, Sprite background, Sprite icon,
        Color tint, Color labelColor, Vector2 size, Vector2 position, float labelSize)
    {
        RectTransform rect = Box(parent, name, size, position);

        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = background;
        image.color = tint;
        image.type = Image.Type.Sliced;
        image.raycastTarget = true;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(AnnouncePress);

        // A visible press state matters more on touch than on desktop: there is no cursor
        // to confirm the tap landed.
        var colors = button.colors;
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.selectedColor = Color.white;
        colors.fadeDuration = 0.06f;
        button.colors = colors;

        bool hasLabel = !string.IsNullOrEmpty(label);

        float inset = size.y * 0.16f;
        if (icon != null)
        {
            float glyph = size.y * 0.56f;
            // An icon sharing the row with a label sits at the left of it. An icon on its
            // own is the button's entire content and belongs in the middle: pinned to the
            // left edge of a wide pill, a lone triangle reads as a "back" affordance
            // pointing out of the screen rather than as the button's action.
            float iconX = hasLabel ? -size.x * 0.5f + inset + glyph * 0.5f : 0f;
            RectTransform iconRect = Box(rect, "Icon", new Vector2(glyph, glyph),
                new Vector2(iconX, 0f));
            Sprite(iconRect, icon, labelColor);
        }

        // Shift the label off-centre only when an icon shares the row, so a label-only
        // button stays centred rather than looking nudged.
        float labelOffset = icon != null && hasLabel ? size.y * 0.30f : 0f;
        Text(rect, "Label", label, labelSize, labelColor,
            new Vector2(size.x - inset * 2f, size.y), new Vector2(labelOffset, 0f));

        return button;
    }

    /// <summary>
    /// An icon-only square button, for the gear and the close cross.
    /// </summary>
    public static Button IconButton(
        Transform parent, string name, Sprite icon, Color tint, float size,
        Vector2 anchor, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.sizeDelta = new Vector2(size, size);
        rect.anchoredPosition = position;

        var image = go.AddComponent<Image>();
        image.sprite = icon;
        image.color = tint;
        image.raycastTarget = true;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(AnnouncePress);

        var colors = button.colors;
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.fadeDuration = 0.06f;
        button.colors = colors;

        return button;
    }

    /// <summary>
    /// A labelled on/off toggle drawn as an icon with a strike-through when off, matching
    /// the reference's sound and music switches.
    /// </summary>
    public static Button IconToggle(
        Transform parent, string name, string label, Sprite icon, Sprite slash,
        Color tint, Vector2 size, Vector2 position, float labelSize, out Image slashImage)
    {
        RectTransform rect = Box(parent, name, size, position);
        rect.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);   // hit area only

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();

        float glyph = size.y * 0.56f;
        RectTransform iconRect = Box(rect, "Icon", new Vector2(glyph, glyph), new Vector2(0f, size.y * 0.14f));
        Sprite(iconRect, icon, tint);

        RectTransform slashRect = Box(rect, "Slash", new Vector2(glyph, glyph), new Vector2(0f, size.y * 0.14f));
        slashImage = Sprite(slashRect, slash, new Color(0.90f, 0.15f, 0.15f, 1f));

        Text(rect, "Label", label, labelSize, tint,
            new Vector2(size.x, size.y * 0.4f), new Vector2(0f, -size.y * 0.32f));

        return button;
    }

    /// <summary>Fades a CanvasGroup in or out. Unscaled so it still runs on a paused game.</summary>
    public static System.Collections.IEnumerator Fade(
        CanvasGroup group, float from, float to, float duration, Action onComplete = null)
    {
        group.alpha = from;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, BlockBlast.Core.Easing.OutQuad(elapsed / duration));
            yield return null;
        }

        group.alpha = to;
        onComplete?.Invoke();
    }
}
