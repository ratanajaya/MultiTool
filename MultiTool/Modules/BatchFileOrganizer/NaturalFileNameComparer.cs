namespace MultiTool.Modules.BatchFileOrganizer;

public sealed class NaturalFileNameComparer : IComparer<string>
{
    public static NaturalFileNameComparer Instance { get; } = new();

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var leftIndex = 0;
        var rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
            {
                var comparison = CompareDigitRuns(left, ref leftIndex, right, ref rightIndex);
                if (comparison != 0)
                {
                    return comparison;
                }

                continue;
            }

            var characterComparison = char.ToUpperInvariant(left[leftIndex])
                .CompareTo(char.ToUpperInvariant(right[rightIndex]));
            if (characterComparison != 0)
            {
                return characterComparison;
            }

            leftIndex++;
            rightIndex++;
        }

        var lengthComparison = (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
        return lengthComparison != 0
            ? lengthComparison
            : StringComparer.Ordinal.Compare(left, right);
    }

    private static int CompareDigitRuns(
        string left,
        ref int leftIndex,
        string right,
        ref int rightIndex)
    {
        var leftStart = leftIndex;
        var rightStart = rightIndex;
        while (leftIndex < left.Length && char.IsDigit(left[leftIndex]))
        {
            leftIndex++;
        }

        while (rightIndex < right.Length && char.IsDigit(right[rightIndex]))
        {
            rightIndex++;
        }

        var leftSignificant = leftStart;
        var rightSignificant = rightStart;
        while (leftSignificant < leftIndex - 1 && left[leftSignificant] == '0')
        {
            leftSignificant++;
        }

        while (rightSignificant < rightIndex - 1 && right[rightSignificant] == '0')
        {
            rightSignificant++;
        }

        var significantLengthComparison = (leftIndex - leftSignificant)
            .CompareTo(rightIndex - rightSignificant);
        if (significantLengthComparison != 0)
        {
            return significantLengthComparison;
        }

        for (var offset = 0; offset < leftIndex - leftSignificant; offset++)
        {
            var digitComparison = left[leftSignificant + offset]
                .CompareTo(right[rightSignificant + offset]);
            if (digitComparison != 0)
            {
                return digitComparison;
            }
        }

        return (leftIndex - leftStart).CompareTo(rightIndex - rightStart);
    }
}
