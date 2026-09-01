using NUnit.Framework;
using UnityEngine;
using BlockBlast.Core;

namespace BlockBlast.Tests
{
    /// <summary>
    /// Layout is the one system that cannot be checked by playing on one machine: it is
    /// correct or broken per device, and the failure mode is a tray half off the bottom of
    /// somebody else's phone. These run it across the real range of shipping aspect ratios
    /// and assert the properties that must hold on all of them.
    /// </summary>
    public class ScreenLayoutTests
    {
        /// <summary>Width / height for screens the game will actually meet, portrait.</summary>
        private static readonly (string name, float aspect)[] Devices =
        {
            ("iPad 4:3",            3f / 4f),
            ("iPad Pro 3:4.3",      0.70f),
            ("Surface 3:2",         2f / 3f),
            ("classic 16:10",       10f / 16f),
            ("classic 16:9",        9f / 16f),
            ("iPhone 8 16:9",       0.5625f),
            ("Pixel 18:9",          0.5f),
            ("iPhone X 19.5:9",     9f / 19.5f),
            ("Galaxy 20:9",         0.45f),
            ("Xperia 21:9",         9f / 21f),
            ("very tall 22:9",      9f / 22f),
            ("square-ish 1:1",      1f)
        };

        // An 8x8 board of unit cells with the project's spacing.
        private static GridGeometry Board() => new GridGeometry(8, 8, 1f, 0.04f, Vector2.zero);

        private static ScreenLayout Fit(float aspect, LayoutConfig config)
        {
            GridGeometry g = Board();
            return ScreenLayout.Compute(g.TotalWidth, g.TotalHeight, g.Pitch, aspect, Vector2.zero, config);
        }

        private static float FramedExtent(LayoutConfig config)
        {
            GridGeometry g = Board();
            return g.TotalWidth + config.Sanitized().boardFramePadding * g.Pitch * 2f;
        }

        /// <summary>
        /// Real cutouts, as fractions of screen height: a modest punch-hole, an iPhone-class
        /// notch with a home indicator, and an exaggerated pair no shipping phone has, to
        /// prove the layout degrades rather than inverts.
        /// </summary>
        private static readonly (string name, float top, float bottom)[] Cutouts =
        {
            ("no cutout",       0f,    0f),
            ("punch-hole",      0.035f, 0f),
            ("notch + bar",     0.055f, 0.035f),
            ("extreme",         0.15f,  0.12f),
        };

        [Test]
        public void TrayClearsTheBottomCutoutOnEveryDevice()
        {
            foreach ((string cut, float top, float bottom) in Cutouts)
            {
                foreach ((string name, float aspect) in Devices)
                {
                    LayoutConfig config = LayoutConfig.Default;
                    config.safeTopFraction = top;
                    config.safeBottomFraction = bottom;

                    ScreenLayout fit = Fit(aspect, config);

                    float screenBottom = -fit.WorldHeight * 0.5f;
                    float barTop = screenBottom + fit.WorldHeight * bottom;
                    float trayBottom = fit.TrayCenter.y - fit.TrayHeight * 0.5f;

                    Assert.GreaterOrEqual(trayBottom, barTop - 1e-3f,
                        $"{name} / {cut}: the tray sits inside the bottom cutout, where a " +
                        "gesture bar would cover it and swallow its touches.");
                }
            }
        }

        [Test]
        public void BoardClearsTheTopCutoutOnEveryDevice()
        {
            foreach ((string cut, float top, float bottom) in Cutouts)
            {
                foreach ((string name, float aspect) in Devices)
                {
                    LayoutConfig config = LayoutConfig.Default;
                    config.safeTopFraction = top;
                    config.safeBottomFraction = bottom;

                    ScreenLayout fit = Fit(aspect, config);
                    float halfBoard = FramedExtent(config) * 0.5f;

                    float screenTop = fit.WorldHeight * 0.5f;
                    float notchBottom = screenTop - fit.WorldHeight * top;
                    float boardTop = fit.BoardCenter.y + halfBoard;

                    Assert.LessOrEqual(boardTop, notchBottom + 1e-3f,
                        $"{name} / {cut}: the board reaches under the top cutout.");
                }
            }
        }

        [Test]
        public void CutoutsShrinkThePlayfieldRatherThanMovingItOffScreen()
        {
            // The failure this guards against is a layout that "fits" by pushing content
            // past an edge instead of giving it less room.
            foreach ((string name, float aspect) in Devices)
            {
                LayoutConfig plain = LayoutConfig.Default;
                LayoutConfig inset = LayoutConfig.Default;
                inset.safeTopFraction = 0.055f;
                inset.safeBottomFraction = 0.035f;

                ScreenLayout a = Fit(aspect, plain);
                ScreenLayout b = Fit(aspect, inset);

                Assert.LessOrEqual(b.TrayHeight, a.TrayHeight + 1e-3f,
                    $"{name}: cutouts should give the tray less room, not more.");
                Assert.Greater(b.TrayHeight, 0f, $"{name}: the tray was squeezed out of existence.");
            }
        }

        [Test]
        public void BoardFitsOnScreenAtEveryAspectRatio()
        {
            LayoutConfig config = LayoutConfig.Default;
            float framed = FramedExtent(config);

            foreach ((string name, float aspect) in Devices)
            {
                ScreenLayout fit = Fit(aspect, config);

                Assert.LessOrEqual(framed, fit.WorldWidth + 1e-3f, $"{name}: board wider than the screen.");
                Assert.LessOrEqual(framed, fit.WorldHeight + 1e-3f, $"{name}: board taller than the screen.");
            }
        }

        [Test]
        public void BoardNeverOverlapsTheTray()
        {
            LayoutConfig config = LayoutConfig.Default;
            float halfBoard = FramedExtent(config) * 0.5f;

            foreach ((string name, float aspect) in Devices)
            {
                ScreenLayout fit = Fit(aspect, config);

                float boardBottom = fit.BoardCenter.y - halfBoard;
                float trayTop = fit.TrayCenter.y + fit.TrayHeight * 0.5f;

                Assert.GreaterOrEqual(boardBottom, trayTop - 1e-3f,
                    $"{name}: board bottom {boardBottom:F2} sits below tray top {trayTop:F2}.");
            }
        }

        [Test]
        public void BoardLeavesRoomForTheHudAboveIt()
        {
            LayoutConfig config = LayoutConfig.Default;
            float halfBoard = FramedExtent(config) * 0.5f;

            foreach ((string name, float aspect) in Devices)
            {
                ScreenLayout fit = Fit(aspect, config);

                float boardTop = fit.BoardCenter.y + halfBoard;
                float screenTop = fit.WorldHeight * 0.5f;

                Assert.Less(boardTop, screenTop,
                    $"{name}: board reaches the top of the screen, leaving no room for the score.");
            }
        }

        [Test]
        public void TrayStaysFullyOnScreen()
        {
            LayoutConfig config = LayoutConfig.Default;

            foreach ((string name, float aspect) in Devices)
            {
                ScreenLayout fit = Fit(aspect, config);

                float trayBottom = fit.TrayCenter.y - fit.TrayHeight * 0.5f;
                Assert.GreaterOrEqual(trayBottom, fit.ScreenBottom - 1e-3f,
                    $"{name}: tray band runs off the bottom of the screen.");
            }
        }

        [Test]
        public void EveryTraySlotAndItsPieceStayOnScreen()
        {
            LayoutConfig config = LayoutConfig.Default;
            const int slots = 3;

            foreach ((string name, float aspect) in Devices)
            {
                ScreenLayout fit = Fit(aspect, config);
                float half = fit.SlotExtent(slots) * 0.5f;

                for (int i = 0; i < slots; i++)
                {
                    Vector2 centre = fit.SlotCenter(i, slots);

                    Assert.GreaterOrEqual(centre.x - half, -fit.WorldWidth * 0.5f - 1e-3f,
                        $"{name}: slot {i} overflows the left edge.");
                    Assert.LessOrEqual(centre.x + half, fit.WorldWidth * 0.5f + 1e-3f,
                        $"{name}: slot {i} overflows the right edge.");
                    Assert.GreaterOrEqual(centre.y - half, fit.ScreenBottom - 1e-3f,
                        $"{name}: slot {i} overflows the bottom edge.");
                }
            }
        }

        [Test]
        public void SlotsAreOrderedLeftToRightAndEvenlySpaced()
        {
            ScreenLayout fit = Fit(9f / 16f, LayoutConfig.Default);
            const int slots = 3;

            float a = fit.SlotCenter(0, slots).x;
            float b = fit.SlotCenter(1, slots).x;
            float c = fit.SlotCenter(2, slots).x;

            Assert.Less(a, b);
            Assert.Less(b, c);
            Assert.AreEqual(b - a, c - b, 1e-3f, "Slot spacing must be uniform.");
            Assert.AreEqual(0f, b, 1e-3f, "The middle slot of three should sit on the centre line.");
        }

        [Test]
        public void TallerScreensGiveTheBoardAWiderShareOfTheWidth()
        {
            // On a narrow phone the width is the binding constraint, so the board should
            // use nearly all of it; on a tablet the band height binds first and the board
            // is inset. Getting this backwards is what makes a tablet build look broken.
            LayoutConfig config = LayoutConfig.Default;
            float framed = FramedExtent(config);

            float tallShare = framed / Fit(9f / 20f, config).WorldWidth;
            float tabletShare = framed / Fit(3f / 4f, config).WorldWidth;

            Assert.Greater(tallShare, tabletShare);
            Assert.LessOrEqual(tallShare, config.boardWidthFraction + 1e-3f);
        }

        [Test]
        public void SlotExtentIsBoundedByTheTrayBandNotJustSlotWidth()
        {
            // A wide, short screen has plenty of slot width but very little tray height.
            // Sizing pieces on width alone would push them out of the band.
            ScreenLayout wide = Fit(1f, LayoutConfig.Default);
            Assert.LessOrEqual(wide.SlotExtent(3), wide.TrayHeight + 1e-3f);
        }

        [Test]
        public void ZeroedConfigFallsBackToUsableBands()
        {
            LayoutConfig sane = new LayoutConfig().Sanitized();

            Assert.Greater(sane.hudFraction, 0f);
            Assert.Greater(sane.trayFraction, 0f);
            Assert.Greater(sane.BoardBandFraction, 0.2f);
            Assert.Greater(sane.boardWidthFraction, 0f);
        }

        [Test]
        public void OversizedBandsAreScaledDownRatherThanSwallowingTheBoard()
        {
            var greedy = new LayoutConfig
            {
                hudFraction = 0.6f,
                trayFraction = 0.6f,
                boardWidthFraction = 0.92f,
                boardBandFill = 0.94f,
                boardFramePadding = 0.22f,
                trayPieceFill = 0.82f
            };

            LayoutConfig sane = greedy.Sanitized();

            Assert.GreaterOrEqual(sane.BoardBandFraction, 0.2f);
            Assert.AreEqual(sane.hudFraction, sane.trayFraction, 1e-3f,
                "Equal bands should stay equal after being scaled down together.");
        }

        [Test]
        public void CameraSizeAlwaysCoversTheBoard()
        {
            LayoutConfig config = LayoutConfig.Default;
            float halfBoard = FramedExtent(config) * 0.5f;

            foreach ((string name, float aspect) in Devices)
            {
                ScreenLayout fit = Fit(aspect, config);
                Assert.GreaterOrEqual(fit.OrthographicSize, halfBoard,
                    $"{name}: orthographic size is smaller than half the board.");
            }
        }
    }
}
