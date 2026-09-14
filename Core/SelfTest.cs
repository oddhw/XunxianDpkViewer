using System.Security.Cryptography;
using System.Text;
using XunxianDpkViewer.Models;

namespace XunxianDpkViewer.Core;

public static class SelfTest
{
    public static string Run(Action<string>? progress = null)
    {
        void Step(string message) => progress?.Invoke(message);

        string[] candidates =
        {
            @"E:\Program Files\腾讯游戏\新寻仙\res",
            @"D:\Program Files\腾讯游戏\新寻仙\res",
            @"C:\Program Files\腾讯游戏\新寻仙\res"
        };
        string root = candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "gui.dpk")))
            ?? throw new DirectoryNotFoundException("没有发现《新寻仙》res 目录。");

        var report = new StringBuilder()
            .AppendLine($"资源目录: {root}")
            .AppendLine($"时间: {DateTimeOffset.Now:O}");

        Step("检查 GUI PNG");
        CheckFile(report, root, "gui.dpk", ".png", data =>
        {
            if (!data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new InvalidDataException("PNG 文件头不正确。");
            return "PNG 文件头正确";
        });
        Step("检查 OGG 音频");
        CheckFile(report, root, "sound.dpk", ".ogg", data =>
        {
            if (!data.AsSpan(0, 4).SequenceEqual("OggS"u8))
                throw new InvalidDataException("OGG 文件头不正确。");
            return "OGG 文件头正确";
        });
        Step("检查 OBJ 模型导出");
        CheckFile(report, root, "obj.dpk", ".pmf", data =>
        {
            PmfMesh mesh = PmfParser.Parse(data);
            string target = Path.Combine(Path.GetTempPath(), "xunxian-dpk-self-test.obj");
            try
            {
                ObjExporter.Export(mesh, target, "self_test");
                if (!File.Exists(target) || new FileInfo(target).Length == 0)
                    throw new InvalidDataException("OBJ 导出结果为空。");
            }
            finally
            {
                File.Delete(target);
            }
            return DescribeMesh(mesh) + "，OBJ 导出正确";
        });
        Step("检查角色 PMF");
        CheckFile(report, root, "cha.dpk", ".pmf", data => DescribeMesh(PmfParser.Parse(data)));
        Step("检查模型贴图链");
        CheckModelTexture(report, root, "obj.dpk", "share/mesh/gx_jzjcxjgwkc_004_h.pmf");
        CheckModelTexture(report, root, "cha.dpk", "share/mesh/cw/mz635_mz_001.pmf");
        CheckModelTexture(report, root, "cha.dpk", "special/zj_tuzinv_042/mesh/hd_001.pmf",
            "special/zj_tuzinv_042/texture/zj_zjhd_042_h.dds");
        CheckModelTexture(report, root, "cha.dpk", "special/zj_tuzinv_042/mesh/mz_001.pmf",
            "special/zj_tuzinv_042/texture/zj_zjmz_042_h.dds");
        CheckSmallScaleModel(report, root, "cha.dpk", "special/zj_tuzinv_042/mesh/hd_001.pmf");
        CheckSmallScaleModel(report, root, "cha.dpk", "special/zj_tuzinv_042/mesh/mz_001.pmf");
        Step("检查组合模型");
        CheckCompositeModel(report, root, "cha.dpk", "special/gw_cwbiyiniaoludi_1313");
        CheckCompositeModel(report, root, "cha.dpk", "special/gw_hlnubing_187");
        CheckCompositeModel(report, root, "cha.dpk", "special/zj_tuzinv_042", maximumParts: 64);
        CheckCompositeModel(report, root, "cha.dpk", "special/zj_waiguonan_025", maximumParts: 64);
        CheckRawModelAnimation(
            report,
            root,
            "cha.dpk",
            "special/gw_denglongguai_128/mesh/st_001.pmf");
        Step("检查技能特效");
        CheckSkillEffect(report, root);
        Step("检查组合 OBJ 导出");
        CheckCompositeObjExport(report, root, "cha.dpk", "special/gw_errenzhuanmao_1692", minimumParts: 3);
        Step("检查完整客户端索引");
        CheckCompleteClientIndex(report, root);
        Step("检查游戏资料与客户端富文本");
        CheckGameInfo(report, Step, root);
        Step("自检完成");
        report.AppendLine("SELF-TEST PASSED");
        return report.ToString();
    }

    private static void CheckGameInfo(StringBuilder report, Action<string> progress, string resourceRoot)
    {
        progress("验证客户端悬浮框边框");
        string tooltipFramePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Client", "frm_tip.png");
        if (!File.Exists(tooltipFramePath))
            throw new FileNotFoundException("Client tooltip frame is missing.", tooltipFramePath);

        byte[] tooltipFrameHeader = new byte[8];
        using (FileStream frameStream = File.OpenRead(tooltipFramePath))
        {
            if (frameStream.Read(tooltipFrameHeader, 0, tooltipFrameHeader.Length) != tooltipFrameHeader.Length ||
                !tooltipFrameHeader.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            {
                throw new InvalidDataException("Client tooltip frame is not a valid PNG.");
            }
        }

        using (var guiReader = new DpkReader(Path.Combine(resourceRoot, "gui.dpk")))
        {
            DpkEntry clientFrame = guiReader.ReadEntries().Single(entry =>
                entry.Path.Equals("image/cmn/frm_tip.png", StringComparison.OrdinalIgnoreCase));
            if (!SHA256.HashData(guiReader.Extract(clientFrame))
                    .SequenceEqual(SHA256.HashData(File.ReadAllBytes(tooltipFramePath))))
            {
                throw new InvalidDataException("Packaged tooltip frame differs from gui.dpk/image/cmn/frm_tip.png.");
            }
        }

        progress("验证客户端提示字体");
        string tooltipFontPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Fonts",
            "fzlth_gb18030.ttf");
        if (!File.Exists(tooltipFontPath))
            throw new FileNotFoundException("Client tooltip font is missing.", tooltipFontPath);

        byte[] tooltipFontHeader = new byte[4];
        using (FileStream fontStream = File.OpenRead(tooltipFontPath))
        {
            if (fontStream.Read(tooltipFontHeader, 0, tooltipFontHeader.Length) != tooltipFontHeader.Length ||
                (!tooltipFontHeader.AsSpan().SequenceEqual(new byte[] { 0, 1, 0, 0 }) &&
                 !tooltipFontHeader.AsSpan().SequenceEqual(Encoding.ASCII.GetBytes("OTTO"))))
            {
                throw new InvalidDataException("Client tooltip font is not a valid TrueType/OpenType font.");
            }
        }

        using (var fontReader = new DpkReader(Path.Combine(resourceRoot, "font.dpk")))
        {
            DpkEntry clientFont = fontReader.ReadEntries().Single(entry =>
                entry.Path.Equals("fzlth_gb18030.ttf", StringComparison.OrdinalIgnoreCase));
            if (!SHA256.HashData(fontReader.Extract(clientFont))
                    .SequenceEqual(SHA256.HashData(File.ReadAllBytes(tooltipFontPath))))
            {
                throw new InvalidDataException("Packaged tooltip font differs from font.dpk/fzlth_gb18030.ttf.");
            }
        }

        progress("加载游戏资料索引");
        GameInfoRepository repository = GameInfoRepository.Load(resourceRoot);
        progress("验证游戏资料分类");
        string[] expectedCategories = { "weapon", "equipment", "yinyang", "wanxiang" };
        string[] missing = expectedCategories
            .Where(id => repository.Categories.All(category => !category.Id.Equals(id, StringComparison.Ordinal)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"Game information categories are missing: {string.Join(", ", missing)}");

        progress("验证客户端阴阳玉附魂、附魔与附灵数据");
        GameYinYangCatalog yinYang = repository.YinYang;
        if (yinYang.Jades.Count != 2 ||
            yinYang.Jades.Any(jade => jade.Record.Tooltip is null) ||
            !yinYang.Jades.Any(jade => jade.Record.Name.Equals("曲灵玉·阴", StringComparison.Ordinal)) ||
            !yinYang.Jades.Any(jade => jade.Record.Name.Equals("曲灵玉·阳", StringComparison.Ordinal)) ||
            !yinYang.Jades.Any(jade => jade.Side.Equals("阴", StringComparison.Ordinal) && jade.Record.Id.Equals("5734", StringComparison.Ordinal)) ||
            !yinYang.Jades.Any(jade => jade.Side.Equals("阳", StringComparison.Ordinal) && jade.Record.Id.Equals("5735", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Client Yin/Yang jade base items were not loaded as a pair.");
        }

        if (yinYang.EnchantGroups.Count < 15 ||
            yinYang.SpiritGroups.Count < 15 ||
            yinYang.SoulGroups.Count < 3 ||
            yinYang.Qualities.Select(quality => quality.Name).SequenceEqual(new[] { "普通", "中级", "高级", "稀有", "极品" }) == false)
        {
            throw new InvalidDataException(
                $"Client Yin/Yang affix groups are incomplete: enchant={yinYang.EnchantGroups.Count}, " +
                $"spirit={yinYang.SpiritGroups.Count}, soul={yinYang.SoulGroups.Count}, " +
                $"qualities={string.Join('/', yinYang.Qualities.Select(quality => quality.Name))}.");
        }

        string[] expectedEmptyJadeHints =
        {
            "您可以点化[附魔诀]或[凝魔水晶]以获得附魔属性",
            "您可以点化[附灵诀]或[汇灵琉璃]以获得附灵属性",
            "您可以点化[附魂诀]或者[仙魂诀]以获得附魂属性"
        };
        if (!yinYang.EmptyAttributeHints.Select(hint => hint.Text).SequenceEqual(expectedEmptyJadeHints) ||
            !yinYang.EmptyAttributeHints.Select(hint => hint.Role).SequenceEqual(new[] { "enchant", "spirit", "soul" }))
        {
            throw new InvalidDataException(
                $"Client empty Yin/Yang jade hints are incorrect: {string.Join('/', yinYang.EmptyAttributeHints.Select(hint => hint.Text))}.");
        }

        GameYinYangAffixGroup? ningEnchant = yinYang.EnchantGroups.FirstOrDefault(group => group.Prefix.Equals("宁形", StringComparison.Ordinal));
        GameYinYangAffixGroup? taiJiSoul = yinYang.SoulGroups.FirstOrDefault(group => group.Prefix.Equals("太极", StringComparison.Ordinal));
        GameYinYangAffixGroup? ningSoul = yinYang.SoulGroups.FirstOrDefault(group => group.Prefix.Equals("宁气", StringComparison.Ordinal));
        report.AppendLine(
            $"Yin/Yang sample groups: 宁形={ningEnchant?.Attributes.Count ?? -1}; " +
            $"太极={taiJiSoul?.Attributes.Count ?? -1}; 宁气={ningSoul?.Attributes.Count ?? -1}");
        if (ningEnchant is null ||
            ningEnchant.Attributes.Count < 110 ||
            taiJiSoul is null || taiJiSoul.Attributes.Count < 50 ||
            ningSoul is null || ningSoul.Attributes.Any(attribute => attribute.Name.Equals("生命上限", StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"Client Yin/Yang attribute tiers did not align: ningEnchant={ningEnchant?.Attributes.Count ?? -1}, " +
                $"taiJiSoul={taiJiSoul?.Attributes.Count ?? -1}, ningSoul={ningSoul?.Attributes.Count ?? -1}; " +
                $"taiJi={string.Join('/', taiJiSoul?.Attributes.Select(attribute => attribute.Name).Distinct() ?? Array.Empty<string>())}; " +
                $"ning={string.Join('/', ningSoul?.Attributes.Select(attribute => attribute.Name).Distinct() ?? Array.Empty<string>())}.");
        }

        IReadOnlyList<GameTooltipLine> jadeTooltip = GameTooltipFormatter.FormatYinYang(
            yinYang.Jades[0].Record,
            hintLines: yinYang.EmptyAttributeHints
                .Select(hint => new GameTooltipHintLine(hint.Text))
                .ToArray());
        string jadeTooltipText = string.Join('\n', jadeTooltip.Select(line => string.Concat(line.Spans.Select(span => span.Text))));
        if (jadeTooltip[0].Spans.Count != 1 ||
            !jadeTooltip[0].Spans[0].Color.Equals(GameTooltipFormatter.StatusColor, StringComparison.Ordinal) ||
            !jadeTooltip.Any(line => line.Spans.Any(span =>
                span.Text.Equals("已绑定", StringComparison.Ordinal) &&
                span.Color.Equals(GameTooltipFormatter.BoundColor, StringComparison.Ordinal))) ||
            !jadeTooltipText.Contains("不可卖店", StringComparison.Ordinal) ||
            !jadeTooltip.Any(line => line.Spans.Any(span =>
                span.Text.Equals("右键单击使用其他能力", StringComparison.Ordinal) &&
                span.Color.Equals(GameTooltipFormatter.CommandColor, StringComparison.Ordinal))) ||
            jadeTooltipText.Contains("装备绑定", StringComparison.Ordinal) ||
            jadeTooltipText.Contains("右键单击装备", StringComparison.Ordinal) ||
            expectedEmptyJadeHints.Any(hint => !jadeTooltip.Any(line =>
                string.Concat(line.Spans.Select(span => span.Text)).Equals(hint, StringComparison.Ordinal) &&
                line.Spans.All(span => span.Color.Equals(GameTooltipFormatter.HintColor, StringComparison.Ordinal)))) ||
            jadeTooltipText.IndexOf("不可卖店", StringComparison.Ordinal) <
                jadeTooltipText.IndexOf("等级：1", StringComparison.Ordinal) ||
            jadeTooltipText.IndexOf("此为阴灵玉", StringComparison.Ordinal) <
                jadeTooltipText.IndexOf("可加锁", StringComparison.Ordinal) ||
            jadeTooltipText.IndexOf(expectedEmptyJadeHints[0], StringComparison.Ordinal) <
                jadeTooltipText.IndexOf("此为阴灵玉", StringComparison.Ordinal) ||
            jadeTooltipText.IndexOf("售店价格：0", StringComparison.Ordinal) <
                jadeTooltipText.IndexOf(expectedEmptyJadeHints[^1], StringComparison.Ordinal) ||
            jadeTooltipText.IndexOf("右键单击使用其他能力", StringComparison.Ordinal) <
                jadeTooltipText.IndexOf("售店价格：0", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Client Yin/Yang jade tooltip wording is not preserved.");
        }

        GameYinYangAttribute? sampleEnchant = ningEnchant.Attributes.FirstOrDefault();
        if (sampleEnchant is null)
        {
            throw new InvalidDataException("No client Yin/Yang enchant attribute was available for tooltip verification.");
        }

        IReadOnlyList<GameTooltipLine> activatedJadeTooltip = GameTooltipFormatter.FormatYinYang(
            yinYang.Jades[0].Record,
            supplementLines: new[] { new GameTooltipSupplementLine("附魔属性", sampleEnchant.Text) });
        string activatedJadeTooltipText = string.Join(
            '\n',
            activatedJadeTooltip.Select(line => string.Concat(line.Spans.Select(span => span.Text))));
        if (!activatedJadeTooltipText.Contains($"附魔属性：{sampleEnchant.Text}", StringComparison.Ordinal) ||
            activatedJadeTooltipText.IndexOf($"附魔属性：{sampleEnchant.Text}", StringComparison.Ordinal) <
                activatedJadeTooltipText.IndexOf("此为阴灵玉", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Activated Yin/Yang attributes were not rendered in the client tooltip wording.");
        }

        GameTooltipHintLine[] partiallyActivatedHints = yinYang.EmptyAttributeHints
            .Where(hint => !hint.Role.Equals("enchant", StringComparison.Ordinal))
            .Select(hint => new GameTooltipHintLine(hint.Text))
            .ToArray();
        IReadOnlyList<GameTooltipLine> partiallyActivatedJadeTooltip = GameTooltipFormatter.FormatYinYang(
            yinYang.Jades[0].Record,
            supplementLines: new[] { new GameTooltipSupplementLine("附魔属性", sampleEnchant.Text) },
            hintLines: partiallyActivatedHints);
        string partiallyActivatedJadeText = string.Join(
            '\n',
            partiallyActivatedJadeTooltip.Select(line => string.Concat(line.Spans.Select(span => span.Text))));
        if (partiallyActivatedJadeText.Contains(expectedEmptyJadeHints[0], StringComparison.Ordinal) ||
            !partiallyActivatedJadeText.Contains(expectedEmptyJadeHints[1], StringComparison.Ordinal) ||
            !partiallyActivatedJadeText.Contains(expectedEmptyJadeHints[2], StringComparison.Ordinal))
        {
            throw new InvalidDataException("Partially activated Yin/Yang jade did not preserve the two remaining client hints.");
        }

        report.AppendLine(
            $"Yin/Yang client data: {yinYang.Jades.Count} jades; " +
            $"{yinYang.EnchantGroups.Count} enchant groups; {yinYang.SpiritGroups.Count} spirit groups; " +
            $"{yinYang.SoulGroups.Count} soul groups; {yinYang.AttributeCount:N0} exact trait tiers");

        progress("验证万象宝盘、星辰宝石与炼卦资料");
        GameWanxiangCatalog wanxiang = repository.Wanxiang;
        string[] expectedGuaNames = { "乾", "坤", "震", "兑", "离", "坎", "艮", "巽" };
        string[] expectedPlateQualities =
        {
            "凡品", "玉品", "灵品", "地品", "天品", "王品", "道品", "玄品", "圣品", "仙品", "神品",
            "荒品", "洪品", "星品", "虚品", "羲品", "昊品", "宙品", "域品", "涅品", "寰品"
        };
        string[] actualPlateQualities = wanxiang.Levels
            .GroupBy(level => level.QualityIndex)
            .OrderBy(group => group.Key)
            .Select(group => group.First().QualityName)
            .ToArray();
        if (wanxiang.Plates.Count != 2 ||
            wanxiang.Levels.Count != 169 ||
            wanxiang.GemSeries.Count != 140 ||
            wanxiang.UpgradeMethods.Count != 34 ||
            !wanxiang.GuaNames.SequenceEqual(expectedGuaNames) ||
            !actualPlateQualities.SequenceEqual(expectedPlateQualities) ||
            wanxiang.NoGuaText != "无" ||
            wanxiang.FireUpgradeTipFormat != "消耗材料:<c:FFB6FF00>[%s]<c>*%d" ||
            wanxiang.FairyUpgradeTipFormat != "消耗材料:<c:FFFF6A00>[%s]<c>*%d" ||
            wanxiang.LevelPetalPaths.Count != 168 ||
            wanxiang.LevelExpPaths.Count != 21 ||
            string.IsNullOrWhiteSpace(wanxiang.InlayBackgroundPath) ||
            string.IsNullOrWhiteSpace(wanxiang.EmptySlotPath) ||
            string.IsNullOrWhiteSpace(wanxiang.SelectionFramePath) ||
            string.IsNullOrWhiteSpace(wanxiang.PlateSlotFramePath) ||
            string.IsNullOrWhiteSpace(wanxiang.LevelBasePath) ||
            string.IsNullOrWhiteSpace(wanxiang.LevelCenterBottomPath) ||
            string.IsNullOrWhiteSpace(wanxiang.LevelCenterTopPath) ||
            string.IsNullOrWhiteSpace(wanxiang.WindowHeaderPath) ||
            string.IsNullOrWhiteSpace(wanxiang.WindowFootPath) ||
            string.IsNullOrWhiteSpace(wanxiang.WindowInnerPath) ||
            string.IsNullOrWhiteSpace(wanxiang.WindowBackgroundPath) ||
            string.IsNullOrWhiteSpace(wanxiang.WindowBottomPath) ||
            string.IsNullOrWhiteSpace(wanxiang.ButtonSpritePath) ||
            !File.Exists(wanxiang.ButtonSpritePath) ||
            !Path.GetFileName(wanxiang.ButtonSpritePath)
                .StartsWith("btn_common-", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(wanxiang.TooltipFramePath))
        {
            throw new InvalidDataException(
                $"Client Wanxiang catalog is incomplete: plates={wanxiang.Plates.Count}, " +
                $"levels={wanxiang.Levels.Count}, gems={wanxiang.GemSeries.Count}, " +
                $"upgrades={wanxiang.UpgradeMethods.Count}, petals={wanxiang.LevelPetalPaths.Count}.");
        }

        GameWanxiangLevelState firstPlateLevel = wanxiang.Levels[0];
        GameWanxiangLevelState finalPlateLevel = wanxiang.Levels[^1];
        IReadOnlyList<GameWanxiangState> noGuaStates = firstPlateLevel.ResolveStates(0, "乾");
        GameWanxiangState? firstScatter = firstPlateLevel.ResolveStates(1, "乾").SingleOrDefault();
        GameWanxiangState? firstGua = firstPlateLevel.ResolveStates(8, "乾").SingleOrDefault();
        GameWanxiangState? finalGua = finalPlateLevel.ResolveStates(8, "乾").FirstOrDefault();
        IReadOnlyList<GameWanxiangState> firstKunStates = firstPlateLevel.ResolveStates(8, "坤");
        if (firstPlateLevel.DisplayName != "凡品0段" || firstPlateLevel.MaxExp != 500 ||
            finalPlateLevel.DisplayName != "寰品8段" ||
            noGuaStates.Count != 0 ||
            firstScatter?.Id != "35285" || firstScatter?.Name != "万象·散卦" ||
            string.IsNullOrWhiteSpace(firstScatter?.IconPath) || !File.Exists(firstScatter?.IconPath) ||
            firstGua?.Name != "万象·一星乾卦" ||
            string.IsNullOrWhiteSpace(firstGua.IconPath) || !File.Exists(firstGua.IconPath) ||
            finalGua?.Name != "万象·二十一星乾卦[8段]" ||
            string.IsNullOrWhiteSpace(finalGua.IconPath) || !File.Exists(finalGua.IconPath) ||
            !firstGua.Description.Contains("生命上限提高7.00%+1500点", StringComparison.Ordinal) ||
            firstKunStates.Count != 2 ||
            firstKunStates.Any(state => state.Name != "万象·一星坤卦"))
        {
            throw new InvalidDataException("Client Wanxiang level or gua state mapping is incorrect.");
        }

        GameWanxiangLevelState hongLevelFive = wanxiang.Levels.Single(level =>
            level.QualityName == "洪品" && level.Segment == 5);
        GameWanxiangState? hongLevelFiveKan = hongLevelFive.ResolveStates(8, "坎").FirstOrDefault();
        const string expectedHongLevelFiveKan =
            "万象·十三星坎卦[5段]，八卦封印全亮且为坎若玄水，移动速度提高5%，对怪物伤害提高156%，" +
            "受到怪物伤害降低12%，攻击力提高493750点，卦象星级和段位可提高状态强度";
        if (hongLevelFive.ClientDisplayText != "品阶（段位）：洪品->5段" ||
            hongLevelFiveKan?.Description != expectedHongLevelFiveKan)
        {
            throw new InvalidDataException("Client Wanxiang level or gua attribute text was not preserved.");
        }

        GameWanxiangGemSeries[] selectableGemSeries = wanxiang.GemSeries
            .Where(series => !series.IsBound)
            .ToArray();
        if (selectableGemSeries.Length != 72 ||
            selectableGemSeries.Any(series => series.Name.Contains("绑定", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Client Wanxiang selectable gem list contains bound items.");
        }

        GameWanxiangGemSeries? latestPhysicalGemSeries = wanxiang.GemSeries.FirstOrDefault(series => series.Id == 133);
        GameWanxiangGem? latestPhysicalGem = latestPhysicalGemSeries?.Resolve("乾");
        if (latestPhysicalGemSeries?.Name != "烁晅宝石" ||
            latestPhysicalGem?.Record.Id != "32277" ||
            latestPhysicalGem.Record.Name != "乾卦之烁晅宝石" ||
            !latestPhysicalGem.AttributeText.Contains("11500%的物理会心几率", StringComparison.Ordinal) ||
            !latestPhysicalGem.AttributeText.Contains("354000点忽略物理防御", StringComparison.Ordinal) ||
            !latestPhysicalGem.AttributeText.Contains("395000点物理攻击力", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Client Wanxiang gem gua or attribute mapping is incorrect.");
        }

        GameInfoRecord plate = wanxiang.Plates[0];
        GameItemTooltip? plateTooltip = plate.Tooltip;
        if (plateTooltip?.Type != "八卦盘" ||
            plateTooltip.Action != "右键单击使用其能力" ||
            !plateTooltip.DescriptionRaw.Contains("三清派仙师依先天八卦之脉象", StringComparison.Ordinal) ||
            !plateTooltip.DescriptionRaw.Contains("<c:FF00FF00>八颗<c>威能无匹的宝石", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Client Wanxiang plate tooltip text was not preserved.");
        }

        GameWanxiangGemSeries? qiuLinSeries = wanxiang.GemSeries.FirstOrDefault(series => series.Id == 125);
        GameWanxiangGem? qiuLinGem = qiuLinSeries?.Resolve("乾");
        if (qiuLinSeries?.Name != "璆琳宝石" || qiuLinGem is null)
        {
            throw new InvalidDataException("Client Wanxiang sample gem series is missing.");
        }

        IReadOnlyList<GameWanxiangAttributeTotal> qiuLinTotals =
            GameWanxiangAttributeAggregator.Aggregate(Enumerable.Repeat(qiuLinGem, 8));
        string[] expectedQiuLinTotals =
        {
            "物理会心几率提高78000%",
            "攻击提高2680000点",
            "忽略物理防御提高2400000点"
        };
        if (!qiuLinTotals.Select(total => total.Text).SequenceEqual(expectedQiuLinTotals))
        {
            throw new InvalidDataException(
                $"Client Wanxiang gem totals are incorrect: {string.Join("; ", qiuLinTotals.Select(total => total.Text))}");
        }

        string[] sampleGemIcons = Enumerable.Repeat(qiuLinGem.Record.ImagePath, 8)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToArray();
        var sampleOverlay = new GameWanxiangTooltipOverlay(
            "坎卦",
            hongLevelFive.ClientDisplayText,
            "炼卦值：695120/1700000",
            qiuLinTotals.Select(total => total.Text).ToArray(),
            sampleGemIcons,
            wanxiang.PlateSlotFramePath);
        IReadOnlyList<GameTooltipLine> samplePlateLines =
            GameTooltipFormatter.FormatWanxiang(plate, sampleOverlay);
        string[] samplePlateText = samplePlateLines
            .Select(line => string.Concat(line.Spans.Select(span => span.Text)))
            .ToArray();
        GameTooltipLine? sampleGemLine = samplePlateLines.FirstOrDefault(line =>
            string.Concat(line.Spans.Select(span => span.Text)) == "宝石：");
        string samplePlateFullText = string.Join('\n', samplePlateText);
        if (samplePlateText.FirstOrDefault() != "万象宝盘" ||
            !samplePlateFullText.Contains("卦象：坎卦\n品阶（段位）：洪品->5段\n炼卦值：695120/1700000", StringComparison.Ordinal) ||
            !samplePlateFullText.Contains("八卦属性\n属性1：物理会心几率提高78000%\n属性2：攻击提高2680000点\n属性3：忽略物理防御提高2400000点", StringComparison.Ordinal) ||
            samplePlateFullText.Contains(expectedHongLevelFiveKan, StringComparison.Ordinal) ||
            sampleGemLine?.IconPaths.Count != 8 ||
            !samplePlateFullText.Contains("售店价格：0\n右键单击使用其能力", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Client Wanxiang tooltip order or gem icon row is incorrect.");
        }

        GameWanxiangUpgradeMethod? firstFire = wanxiang.ResolveUpgradeMethod(0, 1);
        GameWanxiangUpgradeMethod? firstFairy = wanxiang.ResolveUpgradeMethod(0, 2);
        GameWanxiangUpgradeMethod? finalFire = wanxiang.ResolveUpgradeMethod(20, 1);
        GameWanxiangUpgradeMethod? finalFairy = wanxiang.ResolveUpgradeMethod(20, 2);
        if (firstFire?.Name != "真火炼卦" ||
            !firstFire.Material.ItemIds.SequenceEqual(new[] { "5685", "5656" }) ||
            firstFire.Material.DisplayName != "玄纹灵玉" || firstFire.Material.Count != 400 ||
            !firstFire.Outcomes.Select(outcome => outcome.Exp).SequenceEqual(new[] { 800, 1600, 8000 }) ||
            firstFairy?.Name != "仙缘炼卦" ||
            !firstFairy.Material.ItemIds.SequenceEqual(new[] { "5686", "5657" }) ||
            firstFairy.Material.DisplayName != "赤纹宝玉" || firstFairy.Material.Count != 50 ||
            finalFire?.Material.DisplayName != "焜荧灵玉" || finalFire.Material.Count != 30 ||
            finalFairy?.Material.DisplayName != "霜锷宝玉" || finalFairy.Material.Count != 30)
        {
            throw new InvalidDataException("Client Wanxiang upgrade materials or weighted outcomes are incorrect.");
        }

        report.AppendLine(
            $"Wanxiang client data: {wanxiang.Levels.Count} levels; {actualPlateQualities.Length} qualities; " +
            $"{wanxiang.GemSeries.Count} gem series; {wanxiang.UpgradeMethods.Count} upgrade rows; " +
            $"{firstPlateLevel.DisplayName} -> {finalPlateLevel.DisplayName}");

        GameInfoCategory[] empty = repository.Categories
            .Where(category => expectedCategories.Contains(category.Id, StringComparer.Ordinal) && category.RecordCount == 0)
            .ToArray();
        if (empty.Length > 0)
            throw new InvalidDataException($"Game information categories are empty: {string.Join(", ", empty.Select(category => category.Name))}");

        progress("验证装备职业、洗炼与强化资料");
        GameInfoCategory equipmentCategory = repository.Categories
            .First(category => category.Id.Equals("equipment", StringComparison.Ordinal));
        string[] expectedEquipmentStages =
        {
            "凤麟装备", "凤麟灵装备", "羽灵装备", "莲华装备", "风岚装备", "明昼装备", "玉峰装备", "泰宇装备",
            "冠世装备", "帝策装备", "影轩装备", "阐幽装备", "碧月装备", "破军装备", "玄影装备", "星陨装备"
        };
        int[] expectedEquipmentStageCounts =
        {
            24, 24, 128, 64, 64, 64, 64, 64, 64, 64, 64, 96, 64, 64, 96, 64
        };
        if (equipmentCategory.RecordCount != 1072 ||
            !equipmentCategory.Stages.Select(stage => stage.Name).SequenceEqual(expectedEquipmentStages) ||
            !equipmentCategory.Stages.Select(stage => stage.Records.Count).SequenceEqual(expectedEquipmentStageCounts) ||
            equipmentCategory.Stages.Any(stage => int.TryParse(stage.Name, out _)))
        {
            throw new InvalidDataException(
                $"Client equipment progression is incorrect: {string.Join(" -> ", equipmentCategory.Stages.Select(stage => $"{stage.Name}({stage.Records.Count})"))}.");
        }

        GameInfoRecord? equipmentRecord = equipmentCategory.Stages
            .SelectMany(stage => stage.Records)
            .FirstOrDefault(record => record.Tooltip?.RequiredProfessions.Count > 0);
        if (equipmentRecord?.Tooltip is null)
            throw new InvalidDataException("No equipment retained its client profession requirement.");

        string[] professions = equipmentCategory.Stages
            .SelectMany(stage => stage.Records)
            .Where(record => record.Tooltip?.RequiredProfessions.Count > 0)
            .Select(record => GameProfessionFormatter.NormalizeRequirement(record.Tooltip!.RequiredProfessions))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (professions.Length == 0)
        {
            throw new InvalidDataException("No normalized equipment professions were found.");
        }
        report.AppendLine($"Equipment professions: {string.Join(", ", professions)}");

        GameInfoRecord? washRecord = equipmentCategory.Stages
            .SelectMany(stage => stage.Records)
            .FirstOrDefault(record => repository.EquipmentWash.Get(record) is not null);
        if (washRecord is null)
            throw new InvalidDataException("No equipment matched the client wash table.");

        GameInfoRecord? enhancementRecord = equipmentCategory.Stages
            .SelectMany(stage => stage.Records)
            .FirstOrDefault(record => repository.EquipmentEnhancement.Get(record) is not null);
        if (enhancementRecord is null)
            throw new InvalidDataException("No equipment matched the client enhancement table.");

        progress("验证客户端装备随机槽文案");
        GameInfoRecord? yulingEquipment = equipmentCategory.Stages
            .SelectMany(stage => stage.Records)
            .FirstOrDefault(record => record.Id.Equals("60418", StringComparison.Ordinal));
        if (yulingEquipment?.Tooltip is null ||
            !yulingEquipment.Tooltip.BaseRows.Select(row => $"{row.Label}{row.Value}").SequenceEqual(new[]
            {
                "防御力+960",
                "法术抗性+570",
                "力量+860",
                "体魄+370",
                "筋骨+240"
            }) ||
            yulingEquipment.Tooltip.EquipRows.Count != 5 ||
            !yulingEquipment.Tooltip.EquipRows[0].Equals(
                "物理会心伤害上限提高400~800%",
                StringComparison.Ordinal) ||
            yulingEquipment.Tooltip.EquipRows.Skip(1).Any(row =>
                !row.Equals("随机属性", StringComparison.Ordinal)) ||
            yulingEquipment.Tooltip.FeatureRows.Count != 0)
        {
            throw new InvalidDataException("Yuling equipment did not retain the client green random-slot layout.");
        }

        GameInfoRecord? clientNecklace = equipmentCategory.Stages
            .SelectMany(stage => stage.Records)
            .FirstOrDefault(record => record.Id.Equals("60410", StringComparison.Ordinal));
        GameInfoRecord? clientAccessory = equipmentCategory.Stages
            .SelectMany(stage => stage.Records)
            .FirstOrDefault(record => record.Id.Equals("60408", StringComparison.Ordinal));
        if (!string.Equals(clientNecklace?.Tooltip?.Part, "项链", StringComparison.Ordinal) ||
            !string.Equals(clientNecklace?.Tooltip?.ClientTypeText, "项链", StringComparison.Ordinal) ||
            !string.Equals(clientAccessory?.Tooltip?.Part, "饰品", StringComparison.Ordinal) ||
            !string.Equals(clientAccessory?.Tooltip?.ClientTypeText, "饰品(装备唯一)", StringComparison.Ordinal) ||
            !string.Equals(clientAccessory?.Tooltip?.ClientQualityText, "传奇装备", StringComparison.Ordinal) ||
            !string.Equals(clientAccessory?.Tooltip?.Action, "右键单击使用其他能力", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Client accessory labels were not preserved: necklace={clientNecklace?.Tooltip?.ClientTypeText}, " +
                $"accessory={clientAccessory?.Tooltip?.ClientTypeText}, " +
                $"quality={clientAccessory?.Tooltip?.ClientQualityText}, action={clientAccessory?.Tooltip?.Action}.");
        }

        IReadOnlyList<GameTooltipLine> clientAccessoryLines = GameTooltipFormatter.Format(clientAccessory!);
        GameTooltipLine? clientAccessoryTypeLine = clientAccessoryLines.FirstOrDefault(line =>
            line.Spans.Any(span => span.Text.Equals("饰品(装备唯一)", StringComparison.Ordinal)));
        string[] clientAccessoryTextLines = clientAccessoryLines
            .Select(line => string.Concat(line.Spans.Select(span => span.Text)))
            .ToArray();
        if (clientAccessoryLines[0].Spans[0].Color != "#FFDA44FF" ||
            clientAccessoryTypeLine is null ||
            clientAccessoryTypeLine.TrailingSpans.Count != 1 ||
            clientAccessoryTypeLine.TrailingSpans[0].Text != "传奇装备" ||
            clientAccessoryTypeLine.TrailingSpans[0].Color != "#FFDA44FF" ||
            clientAccessoryTextLines.Contains("装备", StringComparer.Ordinal) ||
            clientAccessoryTextLines.Contains("饰品", StringComparer.Ordinal) ||
            !clientAccessoryTextLines.Contains("右键单击使用其他能力", StringComparer.Ordinal))
        {
            throw new InvalidDataException("Client ring title color, type line, quality label, or action is incorrect.");
        }

        GameInfoRecord? fenglinEquipment = equipmentCategory.Stages
            .SelectMany(stage => stage.Records)
            .FirstOrDefault(record => record.Id.Equals("59929", StringComparison.Ordinal));
        if (fenglinEquipment?.Tooltip is null ||
            !fenglinEquipment.Tooltip.FeatureRows.SequenceEqual(new[] { "随机属性" }))
        {
            throw new InvalidDataException("Fenglin equipment did not retain the client orange feature slot.");
        }

        GameInfoRecord[] prefixedEquipmentRows = equipmentCategory.Stages
            .SelectMany(stage => stage.Records)
            .Where(record => record.Tooltip is not null &&
                             (record.Tooltip.EquipRows.Any(row => row.StartsWith("装备：", StringComparison.Ordinal)) ||
                              record.Tooltip.FeatureRows.Any(row => row.StartsWith("特性：", StringComparison.Ordinal))))
            .ToArray();
        if (prefixedEquipmentRows.Length > 0)
        {
            throw new InvalidDataException(
                $"Equipment attribute bodies still contain duplicated client prefixes: " +
                string.Join('/', prefixedEquipmentRows.Select(record => record.Id).Take(20)));
        }

        GameInfoRecord? xingyunEquipment = equipmentCategory.Stages
            .SelectMany(stage => stage.Records)
            .FirstOrDefault(record => record.Id.Equals("31508", StringComparison.Ordinal));
        if (xingyunEquipment?.Tooltip is null ||
            xingyunEquipment.Tooltip.EquipRows.Count != 6 ||
            !xingyunEquipment.Tooltip.EquipRows[0].Equals(
                "法术效果提高1568000~2187000点",
                StringComparison.Ordinal) ||
            xingyunEquipment.Tooltip.EquipRows.Skip(1).Any(row =>
                !row.Equals("随机属性", StringComparison.Ordinal)) ||
            xingyunEquipment.Tooltip.EquipRows.Any(row =>
                row.Contains("玉峰【", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Xingyun equipment still contains stale generated attribute mappings.");
        }

        IReadOnlyList<GameTooltipLine> yulingTooltip = GameTooltipFormatter.Format(yulingEquipment);
        IReadOnlyList<GameTooltipLine> fenglinTooltip = GameTooltipFormatter.Format(fenglinEquipment);
        if (yulingTooltip.Count(line => line.Spans.Any(span =>
                span.Text.Equals("装备：随机属性", StringComparison.Ordinal) &&
                span.Color.Equals(GameTooltipFormatter.EquipColor, StringComparison.Ordinal))) != 4 ||
            !fenglinTooltip.Any(line => line.Spans.Any(span =>
                span.Text.Equals("特性：随机属性", StringComparison.Ordinal) &&
                span.Color.Equals(GameTooltipFormatter.FeatureColor, StringComparison.Ordinal))))
        {
            throw new InvalidDataException("Client random-slot labels or colors are incorrect.");
        }

        report.AppendLine(
            "Equipment tooltip data: mb.dpk legend_equip tables; " +
            "Yuling green equipment slots, Fenglin orange feature slot, and Xingyun mappings passed");

        progress("验证洗炼槽位、权重与消耗");
        GameEquipmentWashRecord? clientWash = repository.EquipmentWash.Get("60418");
        if (clientWash is null || clientWash.Slots.Count < 5)
        {
            throw new InvalidDataException("Client equipment wash slots are incomplete.");
        }

        GameEquipmentWashSlot washSlot2 = clientWash.Slots.First(slot => slot.Index == 2);
        progress($"客户端样本 60418 槽2: {string.Join("/", washSlot2.Candidates.Select(candidate => $"{candidate.Id}:{candidate.Weight}"))}");
        if (!washSlot2.IsWashable ||
            !washSlot2.Candidates.Select(candidate => candidate.Weight / 10000).SequenceEqual(new long[] { 500, 500, 3000, 3000, 3000 }))
        {
            throw new InvalidDataException("Client wash candidate weights for item 60418 slot 2 are incorrect.");
        }

        GameEquipmentWashSlot washSlot5 = clientWash.Slots.First(slot => slot.Index == 5);
        if (washSlot5.Candidates.Count != 5 ||
            !washSlot5.Candidates.Select(candidate => candidate.Id).SequenceEqual(new[] { "504", "505", "506", "507", "508" }) ||
            washSlot5.UsesFeatureColor)
        {
            throw new InvalidDataException("Client wash feature-slot candidates for item 60418 are incorrect.");
        }

        GameEquipmentWashCandidate[] genericLateSlotCandidates = repository.EquipmentWash.Records
            .SelectMany(record => record.Slots)
            .Where(slot => slot.Index >= 5)
            .SelectMany(slot => slot.Candidates)
            .Where(candidate =>
                candidate.Name.Equals("装备", StringComparison.Ordinal) ||
                candidate.Name.Equals("特性", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(candidate.Name))
            .ToArray();
        if (genericLateSlotCandidates.Length > 0)
        {
            throw new InvalidDataException(
                $"Client wash slots 5/6 still contain generic labels: " +
                string.Join('/', genericLateSlotCandidates.Select(candidate => candidate.Id).Distinct()));
        }

        GameEquipmentWashCost? washCost = clientWash.Costs.FirstOrDefault(cost => cost.SlotIndex == 2);
        if (washCost?.Material is null || washCost.Material.Count != 10 ||
            !washCost.Material.ItemIds.SequenceEqual(new[] { "57969", "57968" }))
        {
            throw new InvalidDataException("Client wash material cost for item 60418 is incorrect.");
        }

        GameEquipmentWashRecord? moneyWash = repository.EquipmentWash.Get("31508");
        GameEquipmentWashCost? moneyWashCost = moneyWash?.Costs.FirstOrDefault(cost => cost.SlotIndex == 6);
        if (moneyWashCost is null || moneyWashCost.Money != 10000000 || moneyWashCost.Material is not null)
        {
            throw new InvalidDataException("Client wash money cost for item 31508 slot 6 is incorrect.");
        }

        if (moneyWash is null || !moneyWash.MaxTierMarker.Equals("(满)", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Client wash data or the etc/text.txt max-tier marker for item 31508 is missing.");
        }

        var baselineSimulator = new GameEquipmentSimulator(moneyWash, new Random(31508));
        GameEquipmentSimulationSlot? baselineFixedSlot = baselineSimulator.Slots
            .FirstOrDefault(slot => !slot.Definition.IsWashable);
        if (baselineFixedSlot is null ||
            baselineFixedSlot.TierIndex != 0 ||
            baselineFixedSlot.Candidate.TierTexts.Count == 0 ||
            !baselineFixedSlot.Text.Equals(baselineFixedSlot.Candidate.TierTexts[0], StringComparison.Ordinal) ||
            baselineFixedSlot.Text.Contains('~'))
        {
            throw new InvalidDataException("Unrefined fixed equipment attributes do not start at the client minimum tier.");
        }

        string maximumFixedText = GameEquipmentSimulator.FormatTierText(
            baselineFixedSlot.Candidate,
            baselineFixedSlot.Candidate.TierTexts.Count - 1,
            moneyWash.MaxTierMarker);
        if (!maximumFixedText.Equals(
                $"{baselineFixedSlot.Candidate.TierTexts[^1]}{moneyWash.MaxTierMarker}",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The client max-tier marker was not applied to a fully refined attribute.");
        }

        var simulator = new GameEquipmentSimulator(clientWash, new Random(60418));
        GameEquipmentWashCandidate manualCandidate = washSlot2.Candidates[^1];
        int manualTier = Math.Max(0, manualCandidate.TierTexts.Count - 1);
        if (!simulator.ReplaceSlot(washSlot2.Index, manualCandidate.Id, manualTier) ||
            simulator.ReplaceSlot(washSlot2.Index, "not-a-client-candidate", 0) ||
            simulator.Slots.First(slot => slot.Definition.Index == washSlot2.Index) is not { } manualSlot ||
            manualSlot.Candidate.Id != manualCandidate.Id ||
            !simulator.FormatSlotText(manualSlot).Equals(
                $"{manualCandidate.TierTexts[manualTier]}{clientWash.MaxTierMarker}",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Manual wash selection did not stay within the client slot candidates and tiers.");
        }

        GameEquipmentWashOutcome? pendingWash = simulator.WashSlot(2);
        if (pendingWash is null || simulator.PendingWash is null || simulator.ApplyPendingWash() == false)
        {
            throw new InvalidDataException("Wash simulation did not preserve the pending-then-replace flow.");
        }

        report.AppendLine(
            $"Equipment wash simulator: item 60418; {clientWash.Slots.Count} slots; " +
            $"slot 2 weights 500/500/3000/3000/3000; material {washCost.Material.DisplayName} ×{washCost.Material.Count}; " +
            $"unrefined fixed baseline {baselineFixedSlot.Text}; client max marker {moneyWash.MaxTierMarker}");

        progress("验证精炼事件与最佳火力概率");
        GameEquipmentEnhancementInfo? clientEnhancement = repository.EquipmentEnhancement.Get("60418");
        if (clientEnhancement is null || clientEnhancement.MaterialCount != 5 ||
            clientEnhancement.Money != 10000 || clientEnhancement.FireAccuracy != 2 ||
            clientEnhancement.Events.Count < 5)
        {
            throw new InvalidDataException("Client equipment refinement data for item 60418 is incomplete.");
        }

        IReadOnlyDictionary<string, int> normalWeights = GameEquipmentSimulator.GetEnhancementWeights(
            clientEnhancement,
            fireId: 1,
            perfectHit: false);
        IReadOnlyDictionary<string, int> perfectWeights = GameEquipmentSimulator.GetEnhancementWeights(
            clientEnhancement,
            fireId: 1,
            perfectHit: true);
        if (normalWeights.GetValueOrDefault("2") != 300 ||
            perfectWeights.GetValueOrDefault("2") != 1500 ||
            perfectWeights.GetValueOrDefault(clientEnhancement.BaseEventId) != 7400)
        {
            throw new InvalidDataException("Client refinement event weights or best-fire bonus are incorrect.");
        }

        GameEquipmentEnhancementOutcome? refinement = simulator.Enhance(
            clientEnhancement,
            fireId: 1,
            perfectHit: true);
        if (refinement is null || refinement.Event.Id.Length == 0)
        {
            throw new InvalidDataException("Equipment refinement simulation did not produce a client event.");
        }

        report.AppendLine(
            $"Equipment refinement simulator: item 60418; {clientEnhancement.Events.Count} events; " +
            $"material ×{clientEnhancement.MaterialCount}; best-fire event 2 {normalWeights["2"]} → {perfectWeights["2"]}");

        progress("验证客户端装备升级链");
        GameEquipmentUpgrade? upgrade = repository.EquipmentUpgrades.Steps.FirstOrDefault();
        if (upgrade is null ||
            string.IsNullOrWhiteSpace(upgrade.SourceName) ||
            string.IsNullOrWhiteSpace(upgrade.TargetName) ||
            string.IsNullOrWhiteSpace(repository.EquipmentUpgrades.Source) ||
            repository.EquipmentUpgrades.Count == 0)
        {
            throw new InvalidDataException("No client equipment upgrade chain was loaded.");
        }
        report.AppendLine(
            $"Equipment upgrades: {repository.EquipmentUpgrades.Count:N0} client routes; sample {upgrade.SourceName} -> {upgrade.TargetName}");

        progress("查找客户端富文本样本");
        GameInfoRecord? richTextRecord = repository.Categories
            .SelectMany(category => category.Stages)
            .SelectMany(stage => stage.Records)
            .FirstOrDefault(record => !string.IsNullOrWhiteSpace(record.Tooltip?.DescriptionRaw));
        if (richTextRecord is null)
            throw new InvalidDataException("No game item retained its client rich-text description.");

        progress("验证客户端颜色标记");
        IReadOnlyList<GameRichTextSpan> richTextSpans =
            GameRichTextParser.Parse("<c:FFFFD800>gold<c:><c:FF00FFFF>cyan<c:>");
        if (richTextSpans.Count != 2 ||
            richTextSpans[0].Text != "gold" || richTextSpans[0].Color != "#FFFFD800" ||
            richTextSpans[1].Text != "cyan" || richTextSpans[1].Color != "#FF00FFFF")
        {
            throw new InvalidDataException("Client rich-text color parsing failed.");
        }

        progress("验证原版提示框字段顺序");
        var syntheticTooltip = new GameItemTooltip(
            IconPath: null,
            Name: "原版测试武器",
            Type: "武器",
            Status: "武魂未激活",
            Part: string.Empty,
            RequiredProfessions: new[] { "法师", "幽冥" },
            Level: "200",
            Bound: true,
            Quality: "red",
            BaseRows: new[] { new GameInfoDetailRow("伤害力", "+2400") },
            EquipRows: new[] { "法术效果提高9000点" },
            FeatureRows: new[] { "随机属性" },
            RandomRows: new[] { "强化：不应显示" },
            Flags: new[] { "不可卖店", "可加锁" },
            Description: "普通说明",
            DescriptionRaw: "使用[ <c:FFFFFF00>天机夔龙玉佩<c:> ]\\n<c:FF00FFFF>炼气化神。<c:>",
            UseText: "十方阵纹：不应显示",
            Durability: "100",
            SellPrice: "0",
            Action: "鼠标单击使用");
        var syntheticRecord = new GameInfoRecord(
            "1",
            syntheticTooltip.Name,
            null,
            "测试",
            string.Empty,
            syntheticTooltip);
        IReadOnlyList<GameTooltipLine> formattedTooltip = GameTooltipFormatter.Format(syntheticRecord);
        string formattedText = string.Join(
            '\n',
            formattedTooltip.Select(line => string.Concat(line.Spans.Select(span => span.Text))));

        if (formattedTooltip.Count == 0 ||
            formattedTooltip[0].Alignment != GameTooltipAlignment.Center ||
            formattedTooltip[0].Spans.Count != 1 ||
            formattedTooltip[0].Spans[0].Color != "#FFFF0000")
        {
            throw new InvalidDataException("Client tooltip title alignment or quality color is incorrect.");
        }

        if (!formattedTooltip.Any(line =>
                line.Spans.Any(span => span.Text == "已绑定" && span.Color == GameTooltipFormatter.BoundColor)) ||
            !formattedTooltip.Any(line =>
                line.Spans.Any(span => span.Text == "装备：法术效果提高9000点" && span.Color == GameTooltipFormatter.EquipColor)) ||
            !formattedTooltip.Any(line =>
                line.Spans.Any(span => span.Text == "特性：随机属性" && span.Color == GameTooltipFormatter.FeatureColor)) ||
            !formattedTooltip.Any(line =>
                line.Spans.Any(span => span.Text == "天机夔龙玉佩" && span.Color == "#FFFFFF00")) ||
            !formattedTooltip.Any(line =>
                line.Spans.Any(span => span.Text == "炼气化神。" && span.Color == "#FF00FFFF")))
        {
            throw new InvalidDataException("Client tooltip semantic colors are incorrect.");
        }

        if (formattedText.Contains("强化：不应显示", StringComparison.Ordinal) ||
            formattedText.Contains("十方阵纹：不应显示", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Independent enhancement or formation data leaked into the base item tooltip.");
        }

        string[] formattedLines = formattedTooltip
            .Select(line => string.Concat(line.Spans.Select(span => span.Text)))
            .ToArray();
        string[] orderedFields =
        {
            "原版测试武器",
            "已绑定",
            "武器",
            "武魂未激活",
            "需要：法师 幽冥",
            "等级：200",
            "伤害力：+2400",
            "装备：法术效果提高9000点",
            "特性：随机属性",
            "不可卖店",
            "可加锁",
            "使用[ 天机夔龙玉佩 ]",
            "炼气化神。",
            "耐久：100",
            "售店价格：0",
            "鼠标单击使用"
        };
        int previousIndex = -1;
        foreach (string field in orderedFields)
        {
            int currentIndex = -1;
            for (int index = previousIndex + 1; index < formattedLines.Length; index++)
            {
                if (!formattedLines[index].Equals(field, StringComparison.Ordinal))
                    continue;

                currentIndex = index;
                break;
            }

            if (currentIndex < 0)
                throw new InvalidDataException($"Client tooltip field order is incorrect near '{field}'.");
            previousIndex = currentIndex;
        }

        progress("验证真实武器原版提示框");
        GameInfoRecord? referenceWeapon = repository.Categories
            .SelectMany(category => category.Stages)
            .SelectMany(stage => stage.Records)
            .FirstOrDefault(record => record.Id.Equals("62072", StringComparison.Ordinal));
        if (referenceWeapon?.Tooltip is null ||
            !referenceWeapon.Name.Equals("夺天斩月之陨霆锤", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The original client tooltip reference weapon is missing.");
        }

        IReadOnlyList<GameTooltipLine> referenceTooltip = GameTooltipFormatter.Format(referenceWeapon);
        string[] referenceLines = referenceTooltip
            .Select(line => string.Concat(line.Spans.Select(span => span.Text)))
            .ToArray();
        string referenceText = string.Join('\n', referenceLines);
        string[] referenceFields =
        {
            "夺天斩月之陨霆锤",
            "已绑定",
            "武器",
            "武魂未激活",
            "等级：190",
            "伤害力：+10000",
            "体魄：+2500",
            "力量：+1050",
            "筋骨：+1000",
            "装备：攻击提高85000点",
            "装备：忽略物理防御提高15750点",
            "装备：物理会心几率提高135%",
            "装备：物理会心伤害上限提高252%",
            "装备：最终伤害与治疗输出提高50%",
            "不可卖店",
            "可加锁",
            "耐久：100",
            "售店价格：0",
            "鼠标单击使用"
        };
        int referencePreviousIndex = -1;
        foreach (string field in referenceFields)
        {
            int currentIndex = Array.FindIndex(
                referenceLines,
                referencePreviousIndex + 1,
                line => line.Equals(field, StringComparison.Ordinal));
            if (currentIndex < 0)
                throw new InvalidDataException($"Original client reference tooltip is incorrect near '{field}'.");
            referencePreviousIndex = currentIndex;
        }

        if (referenceText.Contains("伤害提高10920点", StringComparison.Ordinal) ||
            referenceWeapon.Tooltip.RandomRows.Any(row => referenceText.Contains(row, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Independent formation or enhancement data leaked into the reference tooltip.");
        }

        progress("验证客户端武魂强化链");
        GameInfoRecord? wuhunWeapon = repository.Categories
            .First(category => category.Id.Equals("weapon", StringComparison.Ordinal))
            .Stages
            .SelectMany(stage => stage.Records)
            .FirstOrDefault(record => record.Id.Equals("64379", StringComparison.Ordinal));
        if (wuhunWeapon?.Weapon is null ||
            !wuhunWeapon.Weapon.LevelLabel.Equals("260 级武器", StringComparison.Ordinal) ||
            !wuhunWeapon.Group.Equals("游侠系", StringComparison.Ordinal) ||
            !wuhunWeapon.Weapon.SupportsWuhun)
        {
            throw new InvalidDataException("The reference weapon level, profession, or Wuhun metadata is missing.");
        }

        GameWeaponEnhancementCatalog enhancement = repository.WeaponEnhancement;
        IReadOnlyList<GameWeaponEnhancementRow> enhancementRows = enhancement.GetRows(wuhunWeapon);
        GameWeaponEnhancementRow? firstEnhancement = enhancementRows.FirstOrDefault();
        GameWeaponEnhancementCost? firstCost = firstEnhancement is null
            ? null
            : enhancement.FindCost(firstEnhancement);
        if (enhancement.ItemCount < 100 ||
            enhancement.CostCount < 200 ||
            firstEnhancement is null ||
            firstEnhancement.StepIndex != 0 ||
            firstEnhancement.Level != 1 ||
            firstEnhancement.RankName != "凡品" ||
            !firstEnhancement.Traits.Contains("伤害提高20点", StringComparer.Ordinal) ||
            firstCost?.MaterialText != "青冥石 ×5" ||
            firstCost.BoundGold != "5砖" ||
            firstCost.SuccessValue != 10 ||
            firstCost.MaterialIcon?.ImagePath is null ||
            !File.Exists(firstCost.MaterialIcon.ImagePath) ||
            !enhancement.AttributeSource.Contains("new_weapon_data.txt", StringComparison.Ordinal) ||
            !enhancement.CostSource.Contains("new_weapon_lvl.txt", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The client Wuhun attributes, costs, or material icon mapping is incomplete: " +
                $"items={enhancement.ItemCount}; costs={enhancement.CostCount}; " +
                $"step={firstEnhancement?.StepIndex}; level={firstEnhancement?.Level}; rank={firstEnhancement?.RankName}; " +
                $"traits={string.Join('/', firstEnhancement?.Traits ?? Array.Empty<string>())}; " +
                $"material={firstCost?.MaterialText}; gold={firstCost?.BoundGold}; success={firstCost?.SuccessValue}; " +
                $"icon={firstCost?.MaterialIcon?.ImagePath}.");
        }

        bool hasInvalidLevelRange = enhancementRows
            .GroupBy(row => row.RankIndex)
            .Any(group => group.Count() != 10 ||
                          !group.Select(row => row.Level).OrderBy(level => level).SequenceEqual(Enumerable.Range(1, 10)));
        if (hasInvalidLevelRange || enhancementRows.Any(row => row.Level is < 1 or > 10))
        {
            throw new InvalidDataException("The client Wuhun levels are not normalized to 1-10 per rank.");
        }

        string[] expectedFirstRankLabels =
        {
            "凡品一阶", "凡品二阶", "凡品三阶", "凡品四阶", "凡品五阶",
            "凡品六阶", "凡品七阶", "凡品八阶", "凡品九阶", "凡品十阶"
        };
        if (!enhancementRows
                .Where(row => row.RankIndex == 0)
                .OrderBy(row => row.Level)
                .Select(row => row.ClientDisplayName)
                .SequenceEqual(expectedFirstRankLabels))
        {
            throw new InvalidDataException("Client Wuhun level labels are not using Chinese numerals from first through tenth rank.");
        }

        GameWeaponEnhancementRow? derivativeEnhancement = enhancementRows
            .FirstOrDefault(row => row.RankName.Equals("衍品", StringComparison.Ordinal));
        GameWeaponEnhancementRow? lastHuanRank = enhancementRows
            .LastOrDefault(row => row.RankName.Equals("寰品", StringComparison.Ordinal));
        GameWeaponEnhancementCost? derivativeCost = derivativeEnhancement is null
            ? null
            : enhancement.FindCost(derivativeEnhancement);
        if (enhancementRows.Count < 220 ||
            derivativeEnhancement is null ||
            derivativeEnhancement.StepIndex != 210 ||
            derivativeEnhancement.Level != 1 ||
            derivativeEnhancement.ClientDisplayName != "衍品一阶" ||
            derivativeEnhancement.Traits.Count == 0 ||
            lastHuanRank?.StepIndex != 209 ||
            lastHuanRank.Level != 10 ||
            lastHuanRank.ClientDisplayName != "寰品十阶" ||
            derivativeCost?.MaterialText != "天宸石 ×80 + 赤金宝珠 ×15")
        {
            throw new InvalidDataException("The client Wuhun derivative rank or its transition material is missing.");
        }

        var enhancedOverlay = new GameWeaponTooltipOverlay(
            "武魂等级：衍品一阶",
            derivativeEnhancement.Traits);
        IReadOnlyList<GameTooltipLine> enhancedTooltip = GameTooltipFormatter.Format(
            wuhunWeapon,
            enhancedOverlay);
        string enhancedText = string.Join(
            '\n',
            enhancedTooltip.Select(line => string.Concat(line.Spans.Select(span => span.Text))));
        if (!enhancedText.Contains("武魂等级：衍品一阶", StringComparison.Ordinal) ||
            !derivativeEnhancement.Traits.All(trait => enhancedText.Contains($"强化：{trait}", StringComparison.Ordinal)) ||
            enhancedText.Contains("武魂未激活", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The selected Wuhun attributes are not reflected in the weapon tooltip.");
        }

        report.AppendLine(
            "Game information: " +
            string.Join("; ", repository.Categories
                .Where(category => expectedCategories.Contains(category.Id, StringComparer.Ordinal))
                .Select(category => $"{category.Name} {category.RecordCount:N0}")));
        report.AppendLine(
            $"Game tooltip frame: {Path.GetFileName(tooltipFramePath)}; " +
            "client NineGrid 12,12,13,13; content margin 6,6,6,7");
        report.AppendLine(
            $"Game tooltip font: {Path.GetFileName(tooltipFontPath)}; client art face at 14px");
        report.AppendLine(
            $"Game tooltip rich text: {richTextRecord.Name}; {richTextSpans.Count} exact color spans; " +
            $"{formattedTooltip.Count} ordered base-tooltip lines; reference item {referenceWeapon.Id}");
        report.AppendLine(
            $"Wuhun enhancement: {enhancement.ItemCount:N0} client weapon chains; " +
            $"{enhancement.CostCount:N0} client cost rows; {wuhunWeapon.Name}; " +
            $"{enhancementRows.Count:N0} capped levels through {derivativeEnhancement.RankName}; " +
            $"transition material {derivativeCost.MaterialText}; tooltip overlay passed");
    }

    private static void CheckFile(
        StringBuilder report,
        string root,
        string archiveName,
        string extension,
        Func<byte[], string> validate)
    {
        using var reader = new DpkReader(Path.Combine(root, archiveName));
        IReadOnlyList<Models.DpkEntry> entries = reader.ReadEntries();
        Models.DpkEntry sample = entries.First(entry => entry.Path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        byte[] data = reader.Extract(sample);
        report.AppendLine($"{archiveName}: {entries.Count:N0} 项；{sample.Path}；{data.Length:N0} 字节；{validate(data)}");
    }

    private static void CheckSkillEffect(StringBuilder report, string root)
    {
        using var workspace = new DpkWorkspace();
        workspace.OpenSingleArchive(Path.Combine(root, "gfx.dpk"));
        AssetEntry effect = workspace.Assets.FirstOrDefault(asset =>
                                asset.Name.Equals("jn_wjingdianbd_002.gfx", StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidDataException("The GFX color regression sample was not found.");
        GfxEffectPreviewData preview =
            GfxEffectLoader.Load(workspace, effect, workspace.Extract(effect));
        int coloredLayers = preview.Layers.Count(layer => layer.ColorKeys.Count > 0);
        int alphaLayers = preview.Layers.Count(layer => layer.AlphaKeys.Count > 0);
        if (preview.TextureCount == 0 || coloredLayers == 0 || alphaLayers == 0)
            throw new InvalidDataException("The GFX color and alpha tracks were not parsed.");
        if (!preview.Layers.SelectMany(layer => layer.ColorKeys)
                .Any(key => key.Blue > key.Red + 0.2f))
            throw new InvalidDataException("The blue GFX color track was not preserved.");

        AssetEntry fireEffect = workspace.Assets.FirstOrDefault(asset =>
                                    asset.Name.Equals("jn_yhuofeng_001.gfx", StringComparison.OrdinalIgnoreCase))
                                ?? throw new InvalidDataException("The fire GFX color regression sample was not found.");
        GfxEffectPreviewData firePreview =
            GfxEffectLoader.Load(workspace, fireEffect, workspace.Extract(fireEffect));
        if (!firePreview.Layers.SelectMany(layer => layer.ColorKeys)
                .Any(key => key.Red > key.Blue + 0.2f))
            throw new InvalidDataException("The warm GFX color track was not preserved.");

        AssetEntry atlasEffect = workspace.Assets.FirstOrDefault(asset =>
                                     asset.Name.Equals("cj_yyanzhu_001.gfx", StringComparison.OrdinalIgnoreCase))
                                 ?? throw new InvalidDataException("The GFX atlas regression sample was not found.");
        GfxEffectPreviewData atlasPreview =
            GfxEffectLoader.Load(workspace, atlasEffect, workspace.Extract(atlasEffect));
        GfxEffectLayer emitterLayer = atlasPreview.Layers.FirstOrDefault(layer => layer.Emitter is not null)
                                      ?? throw new InvalidDataException("The GFX particle emitter was not parsed.");
        if (Math.Abs(emitterLayer.Emitter!.GenerateInterval - 0.08) > 0.001 ||
            Math.Abs(emitterLayer.Emitter.ParticleLife - 1.0) > 0.001 ||
            emitterLayer.TileFrom != 10 || emitterLayer.TileTo != 15 ||
            emitterLayer.ScaleKeys.Count < 2)
            throw new InvalidDataException("The GFX emitter timing, tile range, or scale track was not preserved.");
        if (!emitterLayer.Frames.Any(frame => frame.UvMaxX - frame.UvMinX < 0.5f))
            throw new InvalidDataException("The GFX atlas UV frames were not cropped.");

        AssetEntry groundRingEffect = workspace.Assets.FirstOrDefault(asset =>
                                          asset.Name.Equals("cj_fdiquan_001.gfx", StringComparison.OrdinalIgnoreCase))
                                      ?? throw new InvalidDataException("The procedural GFX regression sample was not found.");
        GfxEffectPreviewData groundRingPreview =
            GfxEffectLoader.Load(workspace, groundRingEffect, workspace.Extract(groundRingEffect));
        GfxEffectLayer groundRingEmitter = groundRingPreview.Layers.FirstOrDefault(layer =>
            layer.Emitter is not null && layer.Frames.Count > 0)
            ?? throw new InvalidDataException("The nested GFX emitter texture was not parsed.");
        if (!groundRingPreview.Layers.Any(layer => layer.Halo is not null) ||
            Math.Abs(groundRingEmitter.Emitter!.FirstTickTime - 2.0) > 0.001 ||
            Math.Abs(groundRingEmitter.Emitter.GenerateInterval - 0.7) > 0.001 ||
            Math.Abs(groundRingEmitter.Emitter.ParticleLife - 2.0) > 0.001 ||
            !groundRingEmitter.Frames.Any(frame =>
                frame.SourcePath.Contains("ts_yuanhuan_002_h.dds", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The procedural halo or nested GFX emitter timing was not preserved.");

        report.AppendLine(
            $"gfx.dpk effect: {effect.Name}; {preview.Layers.Count:N0} layers; " +
            $"{preview.TextureCount:N0} textures; {coloredLayers:N0} colored layers; " +
            $"{alphaLayers:N0} alpha layers; {preview.MeshReferences.Count:N0} mesh references; " +
            $"{preview.FrameCount:N0} frames; blue/warm colors, atlas UVs, nested emitters, procedural halos, and particle timing passed");
    }

    private static void CheckCompositeModel(
        StringBuilder report,
        string root,
        string archiveName,
        string folderPath,
        int maximumParts = int.MaxValue)
    {
        using var workspace = new DpkWorkspace();
        workspace.OpenSingleArchive(Path.Combine(root, archiveName));
        CompositeModelEntry composite = workspace.FindCompositeModels(
            workspace.ArchivePaths.Single(), folderPath).First();
        if (composite.Parts.Count > maximumParts)
            throw new InvalidDataException(
                $"Composite {folderPath} has {composite.Parts.Count:N0} parts; expected at most {maximumParts:N0}.");
        int texturedParts = composite.Parts.Count(part => part.TextureBinding is not null);
        foreach (CompositeModelPart part in composite.Parts)
            _ = PmfParser.Parse(workspace.Extract(part.MeshAsset));
        CompositeModelDiagnostic diagnostic = composite.Diagnostic;
        if (diagnostic.Parts.Count != composite.Parts.Count)
            throw new InvalidDataException(
                $"{folderPath} 组合诊断部件数不一致：{diagnostic.Parts.Count:N0} / {composite.Parts.Count:N0}。");
        report.AppendLine(
            $"{archiveName} 组合模型: {composite.Name}；{composite.Parts.Count:N0} 个部件；" +
            $"{texturedParts:N0} 个贴图材质；{diagnostic.StatusText}");
    }

    private static void CheckCompositeObjExport(
        StringBuilder report,
        string root,
        string archiveName,
        string folderPath,
        int minimumParts)
    {
        using var workspace = new DpkWorkspace();
        workspace.OpenSingleArchive(Path.Combine(root, archiveName));
        CompositeModelEntry? composite = workspace.FindCompositeModels(workspace.ArchivePaths.Single(), folderPath)
            .FirstOrDefault();
        if (composite is null)
        {
            report.AppendLine($"{archiveName} 组合 OBJ 导出: {folderPath} 未找到，跳过兼容性检查");
            return;
        }

        if (composite.Parts.Count < minimumParts)
            throw new InvalidDataException(
                $"{folderPath} 组合部件过少：{composite.Parts.Count:N0} / {minimumParts:N0}。");

        ObjExporter.ObjPart[] parts = composite.Parts
            .Select((part, index) => new ObjExporter.ObjPart(
                $"{index + 1:000}_{Path.GetFileNameWithoutExtension(part.MeshAsset.Name)}",
                PmfParser.Parse(workspace.Extract(part.MeshAsset)),
                string.IsNullOrWhiteSpace(part.MaterialName)
                    ? Path.GetFileNameWithoutExtension(part.MeshAsset.Name)
                    : part.MaterialName,
                part.TextureBinding?.TextureAsset.Name))
            .ToArray();
        long compositeTriangles = parts.Sum(part => (long)part.Mesh.DeclaredTriangleCount);
        long largestSinglePart = parts.Max(part => (long)part.Mesh.DeclaredTriangleCount);
        if (compositeTriangles <= largestSinglePart)
            throw new InvalidDataException(
                $"{folderPath} 组合 OBJ 面数没有超过单个 PMF：组合 {compositeTriangles:N0}，单件最大 {largestSinglePart:N0}。");

        string target = Path.Combine(Path.GetTempPath(), "xunxian-dpk-composite-self-test.obj");
        string material = Path.ChangeExtension(target, ".mtl");
        try
        {
            ObjExporter.Export(parts, target, composite.Name);
            if (!File.Exists(target) || new FileInfo(target).Length == 0)
                throw new InvalidDataException("组合 OBJ 导出结果为空。");
            int exportedFaces = File.ReadLines(target).Count(line => line.StartsWith("f ", StringComparison.Ordinal));
            if (exportedFaces <= largestSinglePart)
                throw new InvalidDataException(
                    $"{folderPath} 组合 OBJ 导出的面数仍像单部件：{exportedFaces:N0} / {compositeTriangles:N0}。");
        }
        finally
        {
            File.Delete(target);
            File.Delete(material);
        }

        report.AppendLine($"{archiveName} 组合 OBJ 导出: {composite.Name}；{parts.Length:N0} 个部件；{compositeTriangles:N0} 三角面");
    }

    private static void CheckRawModelAnimation(
        StringBuilder report,
        string root,
        string archiveName,
        string modelPath)
    {
        using var workspace = new DpkWorkspace();
        workspace.OpenSingleArchive(Path.Combine(root, archiveName));
        AssetEntry model = workspace.Assets.First(asset =>
            asset.Kind == AssetKind.Model &&
            asset.Entry.Path.Equals(modelPath, StringComparison.OrdinalIgnoreCase));
        ModelAnimationSet? animationSet = workspace.LoadModelAnimationSet(model);
        if (animationSet is null || animationSet.Animations.Count == 0)
            throw new InvalidDataException($"{modelPath} 没有从同目录 CCT 解析到骨骼动画。");

        report.AppendLine(
            $"{archiveName} mesh 动画: {model.Name}；{animationSet.Animations.Count:N0} 个动作；" +
            $"{animationSet.Animations.Sum(animation => animation.FrameCount):N0} 帧");
    }

    private static void CheckModelTexture(
        StringBuilder report,
        string root,
        string archiveName,
        string modelPath,
        string? expectedTexturePath = null)
    {
        using var workspace = new DpkWorkspace();
        workspace.OpenSingleArchive(Path.Combine(root, archiveName));
        AssetEntry model = workspace.Assets.First(asset =>
            asset.Kind == AssetKind.Model && asset.Entry.Path.Equals(modelPath, StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<ModelTextureBinding> bindings = workspace.ResolveModelTextures(model);
        ModelTextureBinding baseMap = bindings.First(binding =>
            binding.MapType.StartsWith("BaseMap", StringComparison.OrdinalIgnoreCase));
        if (expectedTexturePath is not null &&
            !baseMap.TextureAsset.Entry.Path.Equals(expectedTexturePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{modelPath} 默认贴图应为 {expectedTexturePath}，实际为 {baseMap.TextureAsset.Entry.Path}。");
        }

        DecodedTexture texture = DdsDecoder.Decode(workspace.Extract(baseMap.TextureAsset));
        report.AppendLine($"{archiveName} 贴图链: {model.Name} → {baseMap.ConfigPath} → {baseMap.TextureAsset.Entry.Path}；{texture.Width}×{texture.Height} {texture.Format}");
    }

    private static void CheckSmallScaleModel(StringBuilder report, string root, string archiveName, string modelPath)
    {
        using var workspace = new DpkWorkspace();
        workspace.OpenSingleArchive(Path.Combine(root, archiveName));
        AssetEntry model = workspace.Assets.First(asset =>
            asset.Kind == AssetKind.Model && asset.Entry.Path.Equals(modelPath, StringComparison.OrdinalIgnoreCase));
        PmfMesh mesh = PmfParser.Parse(workspace.Extract(model));
        int renderableTriangles = 0;
        for (int offset = 0; offset + 2 < mesh.Indices.Count; offset += 3)
        {
            System.Numerics.Vector3 a = mesh.Vertices[mesh.Indices[offset]];
            System.Numerics.Vector3 b = mesh.Vertices[mesh.Indices[offset + 1]];
            System.Numerics.Vector3 c = mesh.Vertices[mesh.Indices[offset + 2]];
            System.Numerics.Vector3 normal = System.Numerics.Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() > 1e-20f) renderableTriangles++;
        }

        if (renderableTriangles < mesh.DeclaredTriangleCount * 0.9)
            throw new InvalidDataException(
                $"{modelPath} 可渲染三角面过少：{renderableTriangles:N0} / {mesh.DeclaredTriangleCount:N0}。");
        report.AppendLine($"{archiveName} 小尺度模型: {model.Name}；{renderableTriangles:N0} / {mesh.DeclaredTriangleCount:N0} 个三角面可填充");
    }

    private static void CheckCompleteClientIndex(StringBuilder report, string root)
    {
        string[] expectedArchives =
        {
            "cha.dpk", "font.dpk", "gfx.dpk", "gui.dpk", "movie.dpk", "music.dpk", "obj.dpk",
            "scn.dpk", "sky.dpk", "sound.dpk", "system.dpk", "terr.dpk", "water.dpk"
        };
        using var workspace = new DpkWorkspace();
        workspace.OpenClientResourceFolder(root);
        string[] loaded = workspace.ArchivePaths.Select(Path.GetFileName).OfType<string>().ToArray();
        string[] missing = expectedArchives.Except(loaded, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"未加载 DPK：{string.Join(", ", missing)}");
        int fonts = workspace.Assets.Count(asset => asset.Kind == AssetKind.Font);
        if (fonts != 3)
            throw new InvalidDataException($"font.dpk 应包含 3 个字体，实际识别到 {fonts} 个。");
        string? clientRoot = Directory.GetParent(root)?.FullName;
        if (clientRoot is not null && File.Exists(Path.Combine(clientRoot, "mb.dpk")))
        {
            AssetEntry[] mbTables = workspace.Assets
                .Where(asset => asset.Kind == AssetKind.MbTable)
                .ToArray();
            if (mbTables.Length == 0)
                throw new InvalidDataException("已发现 mb.dpk，但没有加载到 MB 表菜单。");
            if (mbTables.Any(asset => !asset.ArchiveName.Equals("mb.dpk", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("MB 表菜单混入了非 mb.dpk 资源。");
            report.AppendLine($"MB 表索引: {mbTables.Length:N0} 个表资源；示例 {mbTables[0].Entry.Path}");
        }
        AssetEntry grass = workspace.Assets.First(asset =>
            asset.ArchiveName.Equals("scn.dpk", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.Equals("grass.byte", StringComparison.OrdinalIgnoreCase));
        ResourceExplanation grassExplanation = ResourceExplanationService.Explain(grass);
        if (grassExplanation.FriendlyName != "草地分布数据" ||
            ResourceExplanationService.GetFolderDisplayName("(3,2)") != "地图分块 X=3，Y=2")
            throw new InvalidDataException("新手说明映射自检失败。");
        report.AppendLine($"完整客户端索引: {loaded.Length:N0} 个 DPK；{workspace.Assets.Count:N0} 个资源；{fonts:N0} 个 TTF 字体");
        report.AppendLine($"新手说明: grass.byte → {grassExplanation.FriendlyName}；(3,2) → 地图分块 X=3，Y=2");
    }

    private static string DescribeMesh(Models.PmfMesh mesh) =>
        $"PMF v{mesh.Version}，{mesh.Vertices.Count:N0} 顶点，{mesh.DeclaredTriangleCount:N0} 三角面";
}
