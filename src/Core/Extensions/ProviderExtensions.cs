using APISwitch.Models;

namespace APISwitch.Extensions;

public static class ProviderExtensions
{
    // 获取供应商实际生效的测试模型:TestModel 为空时回退到全局默认模型。
    // 回退逻辑与 ApiTestService 保持一致。
    public static string GetEffectiveTestModel(this Provider provider, AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(provider.TestModel))
        {
            return provider.TestModel.Trim();
        }

        return settings.GetTestModelByToolType(provider.ToolType);
    }

    public static bool MatchesSearchKeyword(this Provider provider, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return (provider.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (provider.BaseUrl?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (provider.Remark?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
