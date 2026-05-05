namespace ShopManager.Helpers;

public static class TaiwanIdValidator
{
    private static readonly Dictionary<char, int> _letterMap = new()
    {
        {'A',10},{'B',11},{'C',12},{'D',13},{'E',14},{'F',15},{'G',16},{'H',17},
        {'I',34},{'J',18},{'K',19},{'L',20},{'M',21},{'N',22},{'O',35},{'P',23},
        {'Q',24},{'R',25},{'S',26},{'T',27},{'U',28},{'V',29},{'W',32},{'X',30},
        {'Y',31},{'Z',33}
    };

    public static bool Validate(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        id = id.Trim().ToUpper();
        if (id.Length != 10) return false;
        if (!_letterMap.TryGetValue(id[0], out int code)) return false;
        for (int i = 1; i < 10; i++)
            if (!char.IsDigit(id[i])) return false;
        int[] weights = { 1, 9, 8, 7, 6, 5, 4, 3, 2, 1, 1 };
        int[] digits  = new int[11];
        digits[0] = code / 10;
        digits[1] = code % 10;
        for (int i = 2; i <= 10; i++) digits[i] = id[i - 1] - '0';
        return digits.Zip(weights, (d, w) => d * w).Sum() % 10 == 0;
    }
}
