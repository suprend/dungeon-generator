using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class GameTextFontProvider
{
    private const string LegacyFontResourcePath = "Fonts/LiberationSans";
    private const string DefaultTmpFontResourcePath = "Fonts & Materials/LiberationSans SDF";
    private const string DefaultTmpFallbackResourcePath = "Fonts & Materials/LiberationSans SDF - Fallback";
    private const string PreloadedCharacters =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" +
        "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ" +
        "абвгдеёжзийклмнопрстуфхцчшщъыьэюя" +
        " .,:;!?-+/\\()[]{}%№\"'";

    private static Font legacyFont;
    private static TMP_FontAsset tmpFont;

    public static Font LegacyFont
    {
        get
        {
            if (legacyFont == null)
            {
                legacyFont = Resources.Load<Font>(LegacyFontResourcePath);
                PreloadLegacyFontCharacters(legacyFont);
            }

            return legacyFont;
        }
    }

    public static TMP_FontAsset TmpFont
    {
        get
        {
            if (tmpFont == null)
            {
                tmpFont = CreateRuntimeTmpFont();
            }

            return tmpFont;
        }
    }

    public static void ApplyTo(TMP_Text text)
    {
        if (text == null)
        {
            return;
        }

        TMP_FontAsset fontAsset = TmpFont;
        if (fontAsset != null)
        {
            text.font = fontAsset;
        }
    }

    private static TMP_FontAsset CreateRuntimeTmpFont()
    {
        Font sourceFont = LegacyFont;
        if (sourceFont == null)
        {
            return TMP_Settings.defaultFontAsset
                ?? Resources.Load<TMP_FontAsset>(DefaultTmpFontResourcePath);
        }

        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            90,
            9,
            GlyphRenderMode.SDFAA,
            1024,
            1024,
            AtlasPopulationMode.Dynamic);

        if (fontAsset == null)
        {
            return TMP_Settings.defaultFontAsset
                ?? Resources.Load<TMP_FontAsset>(DefaultTmpFontResourcePath);
        }

        fontAsset.name = "LiberationSans Runtime Cyrillic SDF";

        Shader tmpShader = Shader.Find("TextMeshPro/Distance Field");
        if (tmpShader != null && fontAsset.material != null)
        {
            fontAsset.material.shader = tmpShader;
        }

        TMP_FontAsset fallbackFont = Resources.Load<TMP_FontAsset>(DefaultTmpFallbackResourcePath);
        if (fontAsset.fallbackFontAssetTable == null)
        {
            fontAsset.fallbackFontAssetTable = new List<TMP_FontAsset>();
        }

        if (fallbackFont != null && !fontAsset.fallbackFontAssetTable.Contains(fallbackFont))
        {
            fontAsset.fallbackFontAssetTable.Add(fallbackFont);
        }

        fontAsset.TryAddCharacters(PreloadedCharacters, out _);
        return fontAsset;
    }

    private static void PreloadLegacyFontCharacters(Font font)
    {
        if (font == null)
        {
            return;
        }

        font.RequestCharactersInTexture(PreloadedCharacters, 16, FontStyle.Normal);
        font.RequestCharactersInTexture(PreloadedCharacters, 24, FontStyle.Normal);
        font.RequestCharactersInTexture(PreloadedCharacters, 32, FontStyle.Normal);
    }
}
