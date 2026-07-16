using APISwitch.Models;

namespace APISwitch.Services.Parsers;

/// <summary>
/// 会话解析器接口 - 定义解析会话元数据和消息的标准方法
/// </summary>
public interface ISessionParser
{
    /// <summary>
    /// 解析会话元数据
    /// </summary>
    /// <param name="filePath">会话文件路径</param>
    /// <returns>会话元数据,解析失败返回 null</returns>
    SessionMeta? ParseSession(string filePath);

    /// <summary>
    /// 加载会话所有消息
    /// </summary>
    /// <param name="filePath">会话文件路径</param>
    /// <returns>消息列表</returns>
    List<SessionMessage> LoadMessages(string filePath);
}
