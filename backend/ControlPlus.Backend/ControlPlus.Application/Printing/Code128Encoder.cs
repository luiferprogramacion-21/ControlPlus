namespace ControlPlus.Application.Printing;

/// <summary>Code 128 subsets B and C. The output alternates bar/space widths in modules.</summary>
public static class Code128Encoder
{
    private const int StartB = 104;
    private const int StartC = 105;
    private const int Stop = 106;

    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
    ];

    public static bool CanEncode(string? text) => !string.IsNullOrEmpty(text) &&
        text.All(character => character is >= (char)32 and <= (char)126);

    public static IReadOnlyList<int> Values(string text)
    {
        if (!CanEncode(text))
            throw new ArgumentException("El código está vacío o contiene caracteres que Code 128 no admite.", nameof(text));

        var values = new List<int>();
        if (text.Length % 2 == 0 && text.All(char.IsAsciiDigit))
        {
            values.Add(StartC);
            for (var index = 0; index < text.Length; index += 2)
                values.Add((text[index] - '0') * 10 + text[index + 1] - '0');
        }
        else
        {
            values.Add(StartB);
            values.AddRange(text.Select(character => character - 32));
        }

        var checksum = values.Select((value, index) => value * (index == 0 ? 1 : index)).Sum() % 103;
        values.Add(checksum);
        values.Add(Stop);
        return values;
    }

    public static IReadOnlyList<int> Widths(string text) => Values(text)
        .SelectMany(value => Patterns[value].Select(character => character - '0')).ToArray();
}
