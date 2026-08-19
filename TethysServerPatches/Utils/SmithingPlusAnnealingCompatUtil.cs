using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.GameContent;

namespace TethysServerPatches.Utils;

static class SmithingPlusAnnealingCompatUtil
{
    public static readonly Regex TemperatureRangeRegex = new(@"(\s*\(\d+°C\s*-\s*\d+°C\))", RegexOptions.Compiled);

    private static string SetColor(string value, string color) => $"<font color=\"{color}\">{value}</font>";

    private static bool InRange(float temperature, int min, int max) => temperature > min && temperature < max;

    public static void ColorAnnealableRange(StringBuilder dsc, string text, float currentTemp, CollectibleBehaviorQuenchable.MetalPropertyVariant metalProps)
    {
        if (!InRange(currentTemp, metalProps.temperMinTemp, metalProps.temperMaxTemp)) return;
        dsc.Replace(text, TemperatureRangeRegex.Replace(text, m => SetColor(m.Value, "darkcyan")));
    }
}
