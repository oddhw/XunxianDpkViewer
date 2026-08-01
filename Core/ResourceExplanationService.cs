using System.Text.RegularExpressions;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public sealed record ResourceExplanation(
    string FriendlyName,
    string Purpose,
    string UsedWhen,
    string PreviewAdvice,
    string Confidence = "已识别");

public static partial class ResourceExplanationService
{
    private static readonly Dictionary<string, (string Name, string Purpose)> ArchiveDescriptions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["cha.dpk"] = ("角色与生物资源", "角色、怪物、宠物的模型、骨骼、动作和材质"),
            ["font.dpk"] = ("游戏字体", "界面显示文字时使用的字体文件"),
            ["gfx.dpk"] = ("技能与环境特效", "技能光效、粒子、发光和其他视觉效果"),
            ["gui.dpk"] = ("界面与图标", "按钮、窗口、物品图标和界面配置"),
            ["movie.dpk"] = ("过场影片", "登录、剧情或宣传过场动画"),
            ["mb.dpk"] = ("MB 玩法数据表", "物品、任务、技能、宠物、阵营、活动等客户端表数据"),
            ["music.dpk"] = ("背景音乐", "地图与剧情播放的背景音乐"),
            ["obj.dpk"] = ("场景物件", "建筑、石头、树木、武器等静态或共享模型"),
            ["scn.dpk"] = ("地图场景数据", "每张地图的分块、地形高度、草地、水面和摆件位置"),
            ["sky.dpk"] = ("天空环境", "天空盒、天气和远景环境配置"),
            ["sound.dpk"] = ("游戏音效", "战斗、角色、怪物和环境音效"),
            ["system.dpk"] = ("系统公共资源", "客户端多个系统共同使用的小型资源"),
            ["terr.dpk"] = ("地形贴图", "地面、道路、山体等地形材质与混合信息"),
            ["water.dpk"] = ("水体资源", "河流、湖泊的模型、贴图和水面配置")
        };

    public static string GetArchiveDisplayName(string archiveName)
    {
        if (!ArchiveDescriptions.TryGetValue(archiveName, out var description)) return archiveName;
        return $"{description.Name}（{archiveName}）";
    }

    public static string GetFolderDisplayName(string folderName)
    {
        Match coordinate = CoordinateFolderRegex().Match(folderName);
        if (coordinate.Success)
            return $"地图分块 X={coordinate.Groups[1].Value}，Y={coordinate.Groups[2].Value}";
        if (folderName.StartsWith("fb_", StringComparison.OrdinalIgnoreCase))
            return $"副本/地图：{folderName}";
        return folderName.ToLowerInvariant() switch
        {
            "area" => "区域总配置",
            "animation" => "动作动画",
            "astrology" => "星象/命理表",
            "avatar_cupboard" => "外观柜表",
            "camp" => "阵营/营地表",
            "chongziduikang" => "虫子对抗表",
            "cmd" => "命令/指令表",
            "collection" => "收集玩法表",
            "compete" => "竞技玩法表",
            "config" => "模型与材质配置",
            "equip" => "装备表",
            "etc" => "杂项表",
            "grass" => "草地资源",
            "guild" => "帮会/组织表",
            "help" => "帮助说明表",
            "help_bank" => "帮助库表",
            "injury" => "伤害/受伤表",
            "item" => "物品表",
            "life" => "生活技能表",
            "mesh" => "模型部件",
            "myland" => "家园/领地表",
            "new_artifact" => "新法宝/神器表",
            "object" => "对象表",
            "passive" => "被动效果表",
            "pet" => "宠物表",
            "quest" => "任务表",
            "scn" => "场景玩法表",
            "sec_char" => "第二角色/副角色表",
            "server" => "服务端同步表",
            "skill" => "技能表",
            "skeleton" => "骨骼数据",
            "sky" => "天空资源",
            "special" => "独立角色与场景对象",
            "texture" => "贴图材质",
            _ => folderName
        };
    }

    public static ResourceExplanation Explain(AssetEntry asset)
    {
        string name = asset.Name.ToLowerInvariant();
        string extension = asset.Extension.ToLowerInvariant();
        string archive = asset.ArchiveName.ToLowerInvariant();

        ResourceExplanation? known = name switch
        {
            "antiportals.xml" => Known("遮挡优化区域", "记录从某些角度看不到的场景区域，用来减少不必要的绘制。", "客户端优化大型地图的显示性能时读取。", true),
            "beaches.byte" => Known("岸边区域数据", "标记陆地和水面交界附近的区域。", "绘制岸边、浅滩或水陆过渡效果时读取。"),
            "bits_alpha.byte" => Known("地表混合透明度", "控制不同地面贴图在这个地图分块中如何混合。", "客户端绘制泥地、草地、道路交界处时读取。"),
            "citys2.xml" => Known("城市场景配置", "记录这个地图分块使用的城市场景信息。", "进入或加载该地图分块时读取。", true),
            "color.dword" => Known("地表颜色数据", "保存地形顶点的明暗或颜色值，每项通常占 4 字节。", "客户端给地面增加明暗和色彩变化时读取。"),
            "design.xml" => Known("地图设计参数", "记录这张地图或区域的总体设计参数。", "客户端初始化对应地图区域时读取。", true),
            "grass.byte" => Known("草地分布数据", "标记这个地图分块中哪些位置需要生成草。", "客户端绘制草丛和地表植被时读取。"),
            "grass2.byte" => Known("第二层草地分布", "保存另一组草地或植被的分布信息。", "地图使用多种草地效果时读取。"),
            "height.short" => Known("地形高度网格", "保存地面每个采样点的高低，每项占 2 字节。", "客户端生成山坡、坑洼和可行走地面时读取。"),
            "layer.xml" => Known("场景层级配置", "记录场景内容的分层或加载层级。", "客户端分批加载和组织地图内容时读取。", true),
            "mark.xml" => Known("地图标记配置", "保存该区域中的标记或特殊位置说明。", "地图系统需要显示或识别特殊位置时读取。", true, "根据文件名和结构推测"),
            "ornaments2.xml" => Known("场景摆件清单", "记录建筑、树木、石头、装饰物等对象的位置和引用。", "加载这个地图分块中的场景物件时读取。", true),
            "shoal.byte" => Known("浅水区域数据", "标记岸边或浅水区域，帮助区分陆地、浅水和深水。", "绘制岸线或判断水面区域时读取。"),
            "sites2.xml" => Known("场景点位配置", "记录传送点、出生点或其他场景坐标类信息。", "地图逻辑需要定位特殊地点时读取。", true, "根据文件名和结构推测"),
            "sky.xml" => Known("天空与环境配置", "记录这个地图使用的天空、远景或环境参数。", "进入地图并生成天空背景时读取。", true),
            "unitlist.xml" => Known("场景单元清单", "列出该区域需要加载的场景单元或资源。", "客户端组装这个地图区域时读取。", true),
            "unitheader.xml" => Known("地图分块说明", "描述这个分块包含哪些数据以及它们的基本参数。", "客户端开始加载该地图分块时读取。", true),
            "valley.xml" => Known("地形区域配置", "记录山谷、地貌区域或相关环境参数。", "客户端生成该区域的地形环境时读取。", true, "根据文件名和结构推测"),
            "vtx33.byte" => Known("地形顶点辅助数据", "与地形顶点有关的引擎专用数据，准确字段仍需继续逆向。", "生成或渲染地形网格时读取。", false, "部分识别"),
            "water.byte" => Known("水面区域数据", "标记这个地图分块中水面出现的位置。", "客户端绘制河流、湖面或水下效果时读取。"),
            _ => null
        };
        if (known is not null) return known;

        if (asset.Kind == AssetKind.MbTable)
            return ExplainMbTable(asset);

        if (archive == "font.dpk" || extension is ".ttf" or ".otf" or ".ttc")
            return Known("游戏字体文件", "保存游戏界面实际使用的字形。", "界面绘制中文、数字或其他文字时使用。", false);

        return extension switch
        {
            ".xml" => Known("XML 配置文件", "这是人可以阅读的配置，里面通常记录对象、坐标或参数。", "加载对应系统、地图或资源时读取。", true),
            ".cct" => Known("角色/模型组合配置", "把骨骼、模型部件、动作和材质组合成一个完整对象。", "创建角色、怪物或大型场景对象时读取。", true),
            ".cmf" => Known("模型材质配置", "告诉模型应使用哪些 DDS 贴图以及材质参数。", "模型显示颜色和贴图时读取。", true),
            ".gfx" => Known("粒子特效配置", "描述技能、环境或界面粒子效果。", "播放技能光效、烟雾、火焰等效果时读取。"),
            ".mgx" => Known("特效辅助资源", "特效系统使用的模型或效果数据。", "GFX 特效引用它时读取。", false, "部分识别"),
            ".vmm" => Known("特效网格数据", "保存特效使用的网格或运动信息。", "粒子或轨迹特效需要形状时读取。", false, "部分识别"),
            ".fmf" => Known("过场影片文件", "寻仙客户端使用的影片封装格式。", "播放剧情或登录过场动画时读取。"),
            ".paf" or ".pkf" => Known("骨骼动画", "保存角色或怪物的一段动作。", "播放待机、攻击、受击、技能等动作时读取。"),
            ".psf" => Known("模型骨骼", "保存骨骼层级和绑定信息。", "让角色模型正确摆姿势和播放动画时读取。"),
            ".sit" => Known("场景物件放置信息", "记录场景对象的位置或相关参数。", "组装地图场景时读取。", false, "根据扩展名推测"),
            ".tlt" => Known("贴图查找/混合数据", "地形或水体系统使用的贴图辅助数据。", "混合地表或水体材质时读取。", false, "部分识别"),
            ".byte" => Known("8 位二进制数据表", "每一项通常占 1 字节；具体含义由文件名和所在目录决定。", "引擎快速读取地图或效果数据时使用。", false, "仅识别存储方式"),
            ".short" => Known("16 位二进制数据表", "每一项占 2 字节，常用于高度、索引或范围更大的数值。", "加载对应地图分块时读取。", false, "仅识别存储方式"),
            ".dword" => Known("32 位二进制数据表", "每一项占 4 字节，常用于颜色、编号或标记。", "加载对应地图分块时读取。", false, "仅识别存储方式"),
            ".float" => Known("浮点数数据表", "每一项通常是一个 4 字节小数。", "需要坐标、比例或连续参数时读取。", false, "仅识别存储方式"),
            ".bits" => Known("位标记数据", "一个字节可保存多个开关，用来紧凑记录大量是/否状态。", "地图或系统快速判断大量标记时读取。", false, "仅识别存储方式"),
            ".bak" => Known("备份配置", "开发或打包时留下的备份版本，游戏不一定实际使用。", "通常无需普通用户处理。", true, "根据扩展名判断"),
            _ => Known("引擎内部资源", "这是客户端内部使用的专用文件，当前还没有完全解析。", "由所属 DPK 和引用它的配置文件决定。", false, "尚未完全识别")
        };
    }

    public static string GetTechnicalSummary(AssetEntry asset, int byteLength)
    {
        string size = FormatBytes(byteLength);
        if (asset.Kind == AssetKind.MbTable)
            return string.IsNullOrWhiteSpace(asset.Extension)
                ? $"MB 表数据 · {size}"
                : $"{asset.Extension.TrimStart('.').ToUpperInvariant()} MB 表数据 · {size}";
        return asset.Extension.ToLowerInvariant() switch
        {
            ".byte" => $"{size}，约 {byteLength:N0} 个 8 位数据值",
            ".short" => $"{size}，约 {byteLength / 2:N0} 个 16 位数据值",
            ".dword" or ".float" => $"{size}，约 {byteLength / 4:N0} 个 32 位数据值",
            ".bits" => $"{size}，最多可保存 {byteLength * 8L:N0} 个开关标记",
            _ => $"{asset.Extension.TrimStart('.').ToUpperInvariant()} · {size}"
        };
    }

    public static string GetSearchText(AssetEntry asset)
    {
        ResourceExplanation explanation = Explain(asset);
        return $"{explanation.FriendlyName} {explanation.Purpose} {explanation.UsedWhen}";
    }

    public static bool IsTextPreviewSupported(AssetEntry asset) =>
        asset.Kind == AssetKind.MbTable ||
        asset.Extension.ToLowerInvariant() is ".xml" or ".txt" or ".csv" or ".ini" or ".cfg" or ".cct" or ".cmf" or ".sit" or ".ort" or ".cty";

    private static ResourceExplanation ExplainMbTable(AssetEntry asset)
    {
        string normalizedPath = asset.Entry.Path.Replace('\\', '/').Trim('/').ToLowerInvariant();
        string topFolder = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return topFolder switch
        {
            "item" => Known("物品数据表", "记录物品、道具、装备或消耗品相关数据。", "背包、掉落、商店、奖励和装备系统需要显示或判断物品时读取。", true, "根据 MB 目录推测"),
            "equip" => Known("装备数据表", "记录装备部位、属性、限制或相关规则。", "角色穿戴、属性计算和装备展示时读取。", true, "根据 MB 目录推测"),
            "quest" => Known("任务数据表", "记录任务、条件、奖励或流程相关数据。", "任务面板、NPC 对话和任务追踪系统读取。", true, "根据 MB 目录推测"),
            "skill" => Known("技能数据表", "记录技能、效果、等级或施法规则。", "技能栏、战斗计算和技能说明显示时读取。", true, "根据 MB 目录推测"),
            "pet" => Known("宠物数据表", "记录宠物、坐骑或伙伴相关参数。", "宠物展示、培养、召唤和属性系统读取。", true, "根据 MB 目录推测"),
            "camp" => Known("阵营/营地数据表", "记录阵营、营地或势力相关配置。", "阵营关系、活动归属或地图营地逻辑读取。", true, "根据 MB 目录推测"),
            "chongziduikang" => Known("虫子对抗玩法数据表", "记录虫子对抗玩法里的虫子、队伍、技能、奖励、效果和等级配置。", "打开虫子对抗玩法、显示单位或计算玩法数值时读取。", true, "根据 MB 目录推测"),
            "guild" => Known("门派/帮会数据表", "记录组织、帮会或门派玩法相关配置。", "帮会界面、门派玩法或成员系统读取。", true, "根据 MB 目录推测"),
            "compete" => Known("竞技玩法数据表", "记录比赛、竞技或排行类玩法配置。", "竞技活动、匹配和排名展示时读取。", true, "根据 MB 目录推测"),
            "life" => Known("生活技能数据表", "记录采集、制造、生产或生活玩法相关数据。", "生活技能界面、材料判断和产物计算时读取。", true, "根据 MB 目录推测"),
            "object" => Known("对象数据表", "记录客户端玩法对象或交互对象的参数。", "地图交互、NPC、机关或玩法对象需要显示和判断时读取。", true, "根据 MB 目录推测"),
            "server" => Known("服务端同步表", "可能保存客户端与服务端共同使用的编号、规则或显示字段。", "客户端需要和服务端数据保持一致时读取。", false, "根据 MB 目录推测"),
            "scn" => Known("场景玩法表", "记录地图、场景或区域玩法相关数据。", "地图加载、活动区域或场景交互逻辑读取。", true, "根据 MB 目录推测"),
            _ => Known("MB 玩法数据表", "这是 mb.dpk 内的客户端表数据，通常记录玩法、数值、显示文本或编号映射。", "对应系统打开、显示或计算时读取。", true, "根据 MB 包归类")
        };
    }

    private static ResourceExplanation Known(
        string name,
        string purpose,
        string usedWhen,
        bool textPreview = false,
        string confidence = "已识别") =>
        new(name, purpose, usedWhen,
            textPreview ? "可以在下方展开查看格式化后的原始文本。" : "当前以用途说明和技术信息展示，可导出原始文件继续研究。",
            confidence);

    private static string FormatBytes(long length)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = length;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    [GeneratedRegex(@"^\((-?\d+)\s*,\s*(-?\d+)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex CoordinateFolderRegex();
}
