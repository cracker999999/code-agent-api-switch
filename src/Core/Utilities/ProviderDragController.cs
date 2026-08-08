using APISwitch.Models;

namespace APISwitch.Utilities;

public readonly record struct ProviderCardGeometry(double Top, double Height);

public readonly record struct ProviderDragScrollState(
    double ViewportWidth,
    double ViewportHeight,
    double VerticalOffset,
    double MaxVerticalOffset,
    double IndicatorHeight);

public readonly record struct ProviderMoveRequest(int ProviderId, int ToolType, int DestinationIndex);

public sealed class ProviderDragController
{
    private const double AutoScrollEdge = 36;
    private const double DragThreshold = 4;

    private readonly Func<int, ProviderCardGeometry?> _getCardGeometry;
    private readonly Func<ProviderDragScrollState> _getScrollState;
    private readonly Action<double?> _setIndicatorOffset;
    private readonly List<double> _cardMidpoints = new();
    private readonly List<double> _insertionEdges = new();

    private double _dragStartX;
    private double _dragStartY;
    private double _lastViewportX;
    private double _lastViewportY;
    private bool _dragHandlePressed;
    private Provider? _draggedProvider;
    private int _autoScrollDirection;
    private int _pendingInsertionIndex = -1;

    public ProviderDragController(
        Func<int, ProviderCardGeometry?> getCardGeometry,
        Func<ProviderDragScrollState> getScrollState,
        Action<double?> setIndicatorOffset)
    {
        _getCardGeometry = getCardGeometry;
        _getScrollState = getScrollState;
        _setIndicatorOffset = setIndicatorOffset;
    }

    public event Action<bool>? AutoScrollActiveChanged;

    public bool HasActiveDrag => _draggedProvider is not null;
    public bool HasPendingTarget => _pendingInsertionIndex >= 0;

    public void BeginHandlePress(double x, double y)
    {
        _dragStartX = x;
        _dragStartY = y;
        _dragHandlePressed = true;
    }

    public void ReleaseHandle()
    {
        _dragHandlePressed = false;
    }

    public bool TryStartDrag(Provider provider, double x, double y)
    {
        if (!_dragHandlePressed || _draggedProvider is not null)
        {
            return false;
        }

        if (Math.Abs(_dragStartX - x) <= DragThreshold &&
            Math.Abs(_dragStartY - y) <= DragThreshold)
        {
            return false;
        }

        _draggedProvider = provider;
        return true;
    }

    public bool IsDraggingProvider(int providerId)
    {
        return _draggedProvider?.Id == providerId;
    }

    public void UpdateTarget(
        IReadOnlyList<Provider> providers,
        double viewportX,
        double viewportY,
        double contentY)
    {
        if (_draggedProvider is null)
        {
            return;
        }

        var scrollState = _getScrollState();
        UpdateTarget(providers, viewportX, viewportY, contentY, scrollState);
    }

    private void UpdateTarget(
        IReadOnlyList<Provider> providers,
        double viewportX,
        double viewportY,
        double contentY,
        ProviderDragScrollState scrollState)
    {
        _lastViewportX = viewportX;
        _lastViewportY = viewportY;

        // 不限制 X 和 Y 的范围，鼠标离开窗口后仍可依据相对位置继续滚动。
        UpdateAutoScrollDirection(viewportY, scrollState);
        if (!ProviderDragMath.IsInsideViewport(
                viewportX,
                viewportY,
                scrollState.ViewportWidth,
                scrollState.ViewportHeight))
        {
            // 离开列表只清除插入提示，滚动定时器继续根据窗口外的鼠标位置工作。
            ClearTarget();
            return;
        }

        UpdateInsertionTarget(providers, contentY, scrollState);
    }

    public bool TryGetAutoScrollDirection(out int direction)
    {
        direction = _autoScrollDirection;
        return _draggedProvider is not null && direction != 0;
    }

    public void CompleteAutoScrollTick(IReadOnlyList<Provider> providers)
    {
        if (_draggedProvider is null)
        {
            return;
        }

        var scrollState = _getScrollState();
        // 滚动期间鼠标可能静止，需要用最新偏移将视口坐标重新换算为内容坐标。
        UpdateTarget(
            providers,
            _lastViewportX,
            _lastViewportY,
            _lastViewportY + scrollState.VerticalOffset,
            scrollState);
    }

    public ProviderMoveRequest? GetPendingMove(IReadOnlyList<Provider> providers)
    {
        if (_draggedProvider is null || _pendingInsertionIndex < 0)
        {
            return null;
        }

        var draggedIndex = -1;
        for (var index = 0; index < providers.Count; index++)
        {
            if (providers[index].Id == _draggedProvider.Id)
            {
                draggedIndex = index;
                break;
            }
        }

        if (draggedIndex < 0)
        {
            return null;
        }

        var destinationIndex = ProviderDragMath.GetDestinationIndex(_pendingInsertionIndex, draggedIndex);
        if (destinationIndex == draggedIndex)
        {
            return null;
        }

        return new ProviderMoveRequest(
            _draggedProvider.Id,
            _draggedProvider.ToolType,
            destinationIndex);
    }

    public void EndDrag()
    {
        _dragHandlePressed = false;
        _draggedProvider = null;
        SetAutoScrollDirection(0);
        ClearGeometry();
        ClearTarget();
    }

    public void ClearGeometry()
    {
        _cardMidpoints.Clear();
        _insertionEdges.Clear();
    }

    private void UpdateInsertionTarget(
        IReadOnlyList<Provider> providers,
        double contentY,
        ProviderDragScrollState scrollState)
    {
        if (providers.Count == 0)
        {
            ClearTarget();
            return;
        }

        if ((_cardMidpoints.Count != providers.Count ||
             _insertionEdges.Count != providers.Count + 1) &&
            !TryCacheGeometry(providers.Count))
        {
            ClearTarget();
            return;
        }

        // 每个相邻卡片之间只有一个插入槽位，按卡片中线确定鼠标属于哪个槽位。
        _pendingInsertionIndex = ProviderDragMath.GetInsertionIndex(_cardMidpoints, contentY);
        ShowIndicator(_pendingInsertionIndex, scrollState);
    }

    private bool TryCacheGeometry(int providerCount)
    {
        ClearGeometry();

        var previousBottom = 0d;
        for (var index = 0; index < providerCount; index++)
        {
            var geometry = _getCardGeometry(index);
            if (geometry is null)
            {
                ClearGeometry();
                return false;
            }

            var top = geometry.Value.Top;
            var bottom = top + geometry.Value.Height;
            _cardMidpoints.Add((top + bottom) / 2);
            _insertionEdges.Add(index == 0 ? top : (previousBottom + top) / 2);
            previousBottom = bottom;
        }

        _insertionEdges.Add(previousBottom);
        return true;
    }

    private void ShowIndicator(int insertionIndex, ProviderDragScrollState scrollState)
    {
        if (insertionIndex < 0 || insertionIndex >= _insertionEdges.Count)
        {
            _setIndicatorOffset(null);
            return;
        }

        var edgeInViewport = _insertionEdges[insertionIndex] - scrollState.VerticalOffset;
        if (!ProviderDragMath.TryGetIndicatorOffset(
                edgeInViewport,
                scrollState.IndicatorHeight,
                scrollState.ViewportHeight,
                out var indicatorOffset))
        {
            _setIndicatorOffset(null);
            return;
        }

        _setIndicatorOffset(indicatorOffset);
    }

    private void UpdateAutoScrollDirection(double viewportY, ProviderDragScrollState scrollState)
    {
        SetAutoScrollDirection(ProviderDragMath.GetAutoScrollDirection(
            viewportY,
            scrollState.ViewportHeight,
            scrollState.VerticalOffset,
            scrollState.MaxVerticalOffset,
            AutoScrollEdge));
    }

    private void SetAutoScrollDirection(int direction)
    {
        var wasActive = _autoScrollDirection != 0;
        _autoScrollDirection = direction;
        var isActive = direction != 0;
        if (wasActive != isActive)
        {
            AutoScrollActiveChanged?.Invoke(isActive);
        }
    }

    private void ClearTarget()
    {
        _pendingInsertionIndex = -1;
        _setIndicatorOffset(null);
    }
}
