using System;
using UnityEngine;

namespace BlockBlast.Core
{
    /// <summary>
    /// How the screen is divided up, as fractions of its height.
    ///
    /// Expressed as three stacked bands -- HUD, board, tray -- rather than as independent
    /// positions for each element. Independent positions can always be made to overlap or
    /// to leave a hole by a screen shape nobody tested; bands cannot, because they are
    /// defined as shares of the same total.
    /// </summary>
    [Serializable]
    public struct LayoutConfig
    {
        [Tooltip("Share of the screen height reserved above the board for the score readout.")]
        public float hudFraction;

        [Tooltip("Share of the screen height reserved below the board for the tray.")]
        public float trayFraction;

        /// <summary>
        /// Screen height at the top and bottom the layout must keep clear, as fractions of
        /// the whole screen. Notches, punch-holes and gesture bars live there: pixels the
        /// player can see through but that the system may cover or steal touches from.
        ///
        /// Reserved *before* the bands are divided, so every fraction below describes the
        /// usable region rather than the physical screen. Zero on a device without cutouts,
        /// which is why the defaults leave them alone.
        /// </summary>
        public float safeTopFraction;

        public float safeBottomFraction;

        [Tooltip("Most of the screen width the board may take, including its backing.")]
        public float boardWidthFraction;

        [Tooltip("Most of its own band the board may take, so it never fills it edge to edge.")]
        public float boardBandFill;

        [Tooltip("How far the board's backing extends past the cells, in cells. The fit has " +
                 "to account for it, or the framed board ends up wider than asked for.")]
        public float boardFramePadding;

        [Tooltip("Share of a tray slot a piece may fill, leaving a gap between neighbours.")]
        public float trayPieceFill;

        /// <summary>Measured off the reference footage at 9:16.</summary>
        public static LayoutConfig Default => new LayoutConfig
        {
            hudFraction = 0.17f,
            trayFraction = 0.26f,
            boardWidthFraction = 0.92f,
            boardBandFill = 0.94f,
            boardFramePadding = 0.22f,
            trayPieceFill = 0.82f
        };

        /// <summary>The share of the height left for the board once the HUD and tray are taken.</summary>
        public float BoardBandFraction => Mathf.Max(0.2f, 1f - hudFraction - trayFraction);

        public LayoutConfig Sanitized()
        {
            LayoutConfig d = Default;

            float hud = hudFraction > 0f ? hudFraction : d.hudFraction;
            float tray = trayFraction > 0f ? trayFraction : d.trayFraction;

            // Never let the two end bands eat the board. Scale them down together rather
            // than clamping one, so their relative weighting survives.
            float used = hud + tray;
            if (used > 0.75f)
            {
                float shrink = 0.75f / used;
                hud *= shrink;
                tray *= shrink;
            }

            // Clamped hard: a bad safe area reported by an OEM must not be able to squeeze
            // the playfield to nothing.
            float safeTop = Mathf.Clamp(safeTopFraction, 0f, 0.2f);
            float safeBottom = Mathf.Clamp(safeBottomFraction, 0f, 0.2f);

            return new LayoutConfig
            {
                hudFraction = hud,
                trayFraction = tray,
                safeTopFraction = safeTop,
                safeBottomFraction = safeBottom,
                boardWidthFraction = boardWidthFraction > 0f ? Mathf.Clamp01(boardWidthFraction) : d.boardWidthFraction,
                boardBandFill = boardBandFill > 0f ? Mathf.Clamp01(boardBandFill) : d.boardBandFill,
                boardFramePadding = boardFramePadding >= 0f ? boardFramePadding : d.boardFramePadding,
                trayPieceFill = trayPieceFill > 0f ? Mathf.Clamp01(trayPieceFill) : d.trayPieceFill
            };
        }
    }

    /// <summary>
    /// Fits the board and tray to the screen actually in front of the player.
    ///
    /// Hand-placed world positions only line up on the aspect ratio they were authored
    /// against. Phones run from about 4:3 to 21:9 and tablets are squarer still, so on one
    /// device the tray slides off the bottom and on another it overlaps the board.
    ///
    /// The screen is split into a HUD band, a board band and a tray band. The board is
    /// sized to fit inside its own band both across and down -- whichever constraint bites
    /// first wins -- and centred there. Because the bands are shares of one total, the
    /// board and tray cannot collide at any aspect ratio; that is a property of the model
    /// rather than something that has to be re-checked per device.
    ///
    /// Pure and free of Unity's view classes on purpose: layout is otherwise only testable
    /// by building to hardware and squinting, and it is read during initialization by
    /// several components whose Awake order Unity does not define, so it has to be a
    /// function anyone can call at any time and get the same answer.
    /// </summary>
    public readonly struct ScreenLayout
    {
        /// <summary>World-space height the camera shows.</summary>
        public float WorldHeight { get; }

        /// <summary>World-space width the camera shows.</summary>
        public float WorldWidth { get; }

        public Vector2 BoardCenter { get; }
        public Vector2 TrayCenter { get; }

        /// <summary>Height of the tray band in world units.</summary>
        public float TrayHeight { get; }

        private readonly float trayPieceFill;

        public float OrthographicSize => WorldHeight * 0.5f;

        /// <summary>Bottom edge of the visible area, with the camera at the origin.</summary>
        public float ScreenBottom => -WorldHeight * 0.5f;

        private ScreenLayout(
            float worldHeight, float worldWidth, Vector2 boardCenter,
            Vector2 trayCenter, float trayHeight, float trayPieceFill)
        {
            WorldHeight = worldHeight;
            WorldWidth = worldWidth;
            BoardCenter = boardCenter;
            TrayCenter = trayCenter;
            TrayHeight = trayHeight;
            this.trayPieceFill = trayPieceFill;
        }

        /// <summary>Width of one tray slot: the screen divided evenly between the slots.</summary>
        public float SlotWidth(int slotCount) => WorldWidth / Mathf.Max(1, slotCount);

        /// <summary>
        /// Largest square a tray piece may occupy. Bounded by the slot's width *and* the
        /// tray band's height -- a tall piece on a wide screen would otherwise be sized
        /// purely by width and spill out of the band.
        /// </summary>
        public float SlotExtent(int slotCount) =>
            Mathf.Min(SlotWidth(slotCount), TrayHeight) * trayPieceFill;

        /// <summary>Centre of slot <paramref name="index"/>, spread evenly across the width.</summary>
        public Vector2 SlotCenter(int index, int slotCount)
        {
            slotCount = Mathf.Max(1, slotCount);
            float width = SlotWidth(slotCount);

            return new Vector2(TrayCenter.x - WorldWidth * 0.5f + width * (index + 0.5f), TrayCenter.y);
        }

        /// <summary>Distance between neighbouring slot centres.</summary>
        public float SlotSpacing(int slotCount) => SlotWidth(slotCount);

        /// <summary>
        /// Solves the visible world height twice -- once so the board fits across the
        /// screen, once so it fits inside its band -- and keeps the larger, which is the
        /// constraint that actually bites. Using the height rule alone crops a wide board
        /// on a narrow phone; using width alone overflows the band on a tablet.
        /// </summary>
        public static ScreenLayout Compute(
            float boardWidth, float boardHeight, float pitch, float aspect,
            Vector2 cameraCenter, LayoutConfig config)
        {
            config = config.Sanitized();
            aspect = Mathf.Clamp(aspect, 0.2f, 3f);

            // Fit what the player actually sees, which is the board plus its backing.
            float pad = config.boardFramePadding * Mathf.Max(0f, pitch) * 2f;
            float framedWidth = boardWidth + pad;
            float framedHeight = boardHeight + pad;

            // The bands share what is left after the cutouts, not the whole screen.
            float usable = 1f - config.safeTopFraction - config.safeBottomFraction;
            float band = config.BoardBandFraction * config.boardBandFill * usable;
            float fromWidth = framedWidth / config.boardWidthFraction / aspect;
            float fromHeight = framedHeight / band;

            float worldHeight = Mathf.Max(fromWidth, fromHeight);
            float worldWidth = worldHeight * aspect;
            float bottom = cameraCenter.y - worldHeight * 0.5f;

            // The board sits centred in its band, which starts above the tray. Every
            // fraction is measured inside the usable region and then shifted up past the
            // bottom cutout, so a gesture bar pushes the tray up instead of sitting on it.
            float trayHeight = config.trayFraction * usable;
            float bandBottom = config.safeBottomFraction + trayHeight;
            float bandTop = 1f - config.safeTopFraction - config.hudFraction * usable;
            float boardCenterY = (bandBottom + bandTop) * 0.5f;
            float trayCenterY = config.safeBottomFraction + trayHeight * 0.5f;

            return new ScreenLayout(
                worldHeight,
                worldWidth,
                new Vector2(cameraCenter.x, bottom + worldHeight * boardCenterY),
                new Vector2(cameraCenter.x, bottom + worldHeight * trayCenterY),
                worldHeight * trayHeight,
                config.trayPieceFill);
        }

        /// <summary>Convenience overload using the live screen dimensions and a camera at the origin.</summary>
        public static ScreenLayout ForCurrentScreen(GridGeometry geometry, LayoutConfig config)
        {
            float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 9f / 16f;

            // Read the device's own cutouts. Screen.safeArea is the full screen on hardware
            // without any, so this costs nothing there.
            if (Screen.height > 0)
            {
                Rect safe = Screen.safeArea;
                config.safeBottomFraction = Mathf.Max(0f, safe.yMin) / Screen.height;
                config.safeTopFraction = Mathf.Max(0f, Screen.height - safe.yMax) / Screen.height;
            }

            return Compute(geometry.TotalWidth, geometry.TotalHeight, geometry.Pitch, aspect, Vector2.zero, config);
        }
    }
}
