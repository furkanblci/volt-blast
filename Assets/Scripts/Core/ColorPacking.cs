using UnityEngine;

namespace BlockBlast.Core
{
    /// <summary>
    /// Converts between UnityEngine.Color and the packed RGBA32 the board stores.
    ///
    /// The board keeps colours as uint rather than Color so a saved game is 64 plain
    /// integers instead of 64 float quadruples, and so the data model stays something
    /// that can be compared with == and diffed in a save file.
    /// </summary>
    public static class ColorPacking
    {
        /// <summary>Packs to 0xRRGGBBAA. Alpha is forced opaque so a filled cell never packs to 0.</summary>
        public static uint Pack(Color color)
        {
            var c = (Color32)color;
            byte a = c.a == 0 ? (byte)255 : c.a;
            return ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | a;
        }

        public static Color Unpack(uint packed) => new Color32(
            (byte)((packed >> 24) & 0xFF),
            (byte)((packed >> 16) & 0xFF),
            (byte)((packed >> 8) & 0xFF),
            (byte)(packed & 0xFF));

        /// <summary>A filled cell always packs non-zero, so 0 is an unambiguous "empty".</summary>
        public const uint Empty = 0u;
    }
}
