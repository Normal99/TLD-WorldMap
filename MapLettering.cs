using System.Collections.Generic;
using UnityEngine;

namespace WorldMap
{
    internal static partial class MapSession
    {
        private static Texture2D _reverseTexture;
        // Small printed lettering belongs to the paper texture, including the
        // physical item. No world-space UI canvas needs to float above the mesh.
        private static readonly Dictionary<char, string> Lettering = new Dictionary<char, string>
        {
            ['A']="01110100011000111111100011000110001", ['B']="11110100011000111110100011000111110",
            ['C']="01111100001000010000100001000001111", ['D']="11110100011000110001100011000111110",
            ['E']="11111100001000011110100001000011111", ['F']="11111100001000011110100001000010000",
            ['G']="01111100001000010111100011000101111", ['H']="10001100011000111111100011000110001",
            ['I']="11111001000010000100001000010011111", ['J']="00111000100001000010100101001001100",
            ['K']="10001100101010011000101001001010001", ['L']="10000100001000010000100001000011111",
            ['M']="10001110111010110101100011000110001", ['N']="10001110011010110011100011000110001",
            ['O']="01110100011000110001100011000101110", ['P']="11110100011000111110100001000010000",
            ['Q']="01110100011000110001101011001001101", ['R']="11110100011000111110101001001010001",
            ['S']="01111100001000001110000010000111110", ['T']="11111001000010000100001000010000100",
            ['U']="10001100011000110001100011000101110", ['V']="10001100011000110001100010101000100",
            ['W']="10001100011000110101101011010101010", ['X']="10001100010101000100010101000110001",
            ['Y']="10001100010101000100001000010000100", ['Z']="11111000010001000100010001000011111",
            ['0']="01110100011001110101110011000101110", ['1']="00100011000010000100001000010001110",
            ['2']="01110100010000100010001000100011111", ['3']="11110000010000101110000010000111110",
            ['4']="00010001100101010010111110001000010", ['5']="11111100001000011110000010000111110",
            ['6']="01110100001000011110100011000101110", ['7']="11111000010001000100010000100001000",
            ['8']="01110100011000101110100011000101110", ['9']="01110100011000101111000010000101110",
            ['.']="00000000000000000000000000011000110", ['/']="00001000010001000100010001000010000",
            ['-']="00000000000000011111000000000000000"
        };

        private static void PrintText(Color32[] target, string text, int x, int y, int size, Color32 color)
        {
            foreach (char character in text)
            {
                if (Lettering.TryGetValue(character, out var glyph))
                    for (int row = 0; row < 7; row++)
                    for (int column = 0; column < 5; column++)
                    {
                        if (glyph[row * 5 + column] != '1') continue;
                        for (int iy = 0; iy < size; iy++)
                        for (int ix = 0; ix < size; ix++)
                        {
                            int px = x + column * size + ix, py = y + (6 - row) * size + iy;
                            if (px >= 0 && px < W && py >= 0 && py < H) target[py * W + px] = color;
                        }
                    }
                x += size * 6;
            }
        }

        private static void PrintScale()
        {
            var ink = new Color32(98, 89, 66, 255);
            const int length = H / 4;
            for (int y = 22; y < 29; y++)
            for (int x = 32; x < 32 + length; x++)
                if (y == 22 || y == 28 || (x - 32) / (length / 4) % 2 == 0) Put(x, y, ink);
            string label = (_range * .5f / 1000f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " KM";
            PrintText(Pixels, label, 32, 35, 2, ink);
        }

        internal static Texture2D GetReverseTexture()
        {
            if (_reverseTexture != null) return _reverseTexture;
            PreparePaper();
            var pixels = (Color32[])PaperPixels.Clone();
            for (int y = H - 80; y < H - 16; y++)
            for (int x = 20; x < W - 20; x++) pixels[y * W + x] = new Color32(221, 208, 173, 255);
            var ink = new Color32(104, 94, 70, 255);
            PrintText(pixels, "ROUTE NOTES", 32, H - 56, 3, ink);
            PrintText(pixels, "FIELD MAP / REVERSE", 34, H - 73, 1, ink);
            for (int y = 70; y < H - 110; y += 35)
            for (int x = 32; x < W - 32; x++) pixels[y * W + x] = new Color32(180, 171, 141, 255);
            _reverseTexture = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "FieldMapRouteNotes" };
            _reverseTexture.SetPixels32(pixels); _reverseTexture.Apply();
            return _reverseTexture;
        }
    }
}
