namespace APISwitch.Utilities;

public static class ProviderDragMath
{
    public static int GetInsertionIndex(IReadOnlyList<double> cardMidpoints, double pointerY)
    {
        for (var index = 0; index < cardMidpoints.Count; index++)
        {
            if (pointerY < cardMidpoints[index])
            {
                return index;
            }
        }

        return cardMidpoints.Count;
    }

    public static int GetDestinationIndex(int insertionIndex, int draggedIndex)
    {
        // 插入槽位基于移动前的列表；移除源项后，源项之后的槽位需要左移一位。
        return insertionIndex > draggedIndex
            ? insertionIndex - 1
            : insertionIndex;
    }

    public static bool TryGetIndicatorOffset(
        double edgeInViewport,
        double indicatorHeight,
        double viewportHeight,
        out double indicatorOffset)
    {
        indicatorOffset = edgeInViewport - indicatorHeight / 2;
        return edgeInViewport >= 0 && edgeInViewport <= viewportHeight;
    }

    public static bool IsInsideViewport(double x, double y, double viewportWidth, double viewportHeight)
    {
        return x >= 0 && x <= viewportWidth && y >= 0 && y <= viewportHeight;
    }

    public static int GetAutoScrollDirection(
        double pointerY,
        double viewportHeight,
        double verticalOffset,
        double maxVerticalOffset,
        double edgeSize)
    {
        var effectiveEdge = Math.Min(edgeSize, viewportHeight / 4);
        if (pointerY <= effectiveEdge && verticalOffset > 0)
        {
            return -1;
        }

        if (pointerY >= viewportHeight - effectiveEdge && verticalOffset < maxVerticalOffset)
        {
            return 1;
        }

        return 0;
    }
}
