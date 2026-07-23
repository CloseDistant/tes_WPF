namespace RuinaoSoftwareWpf;

/// <summary>
/// 采集工作台 ViewModel 的流程状态与内置模块配置。
/// 先通过 partial 文件把配置从主 VM 中拆出，后续可以继续替换为配置文件、
/// 后端下发配置或临床人员可维护的参数表。
/// </summary>
public sealed partial class AssessmentCaptureViewModel
{
    private enum CaptureWorkbenchStep
    {
        Prepare = 0,
        Demo = 1,
        FaceCheck = 2,
        ModuleExecution = 3,
        Completed = 4
    }

    /// <summary>
    /// 眼动校准单次试次配置。
    /// 固定顺序试次使用 8 个点位，随机显示试次使用 10 个上下区域点。
    /// </summary>
    private sealed record CalibrationTrial(
        int PointCount,
        int FirstCrossMs,
        int NumberMs,
        int LastCrossMs,
        int[] LayoutValues,
        bool IsFixedLayout);

    private sealed record CalibrationFrame(string Text, double X, double Y, TimeSpan Duration);

    private sealed record PictureBrowseItem(string ImagePath, int ImageType);

    private sealed record VideoBrowseItem(string VideoPath, int VideoType);

    private sealed record VoiceBaselineItem(string PromptText, string SyllableName, int SyllableType);

    private sealed record WordReadingGroup(string[] Words, int WordGroupType);

    /// <summary>
    /// 短文朗读段落配置。段落类型只用于事件记录和后续算法分析，不在界面显示。
    /// </summary>
    private sealed record ShortTextReadingPassage(string Text, int PassageType);

    /// <summary>
    /// 情绪问答问题配置。问题类型只用于事件记录和后续算法分析，不在界面显示。
    /// </summary>
    private sealed record EmotionQuestionPrompt(string Text, int QuestionType);

    private sealed record QuestionnaireQuestion(int Number, string Text, string[]? AnswerOptions = null);

    /// <summary>
    /// 问卷模块配置。题目、选项、量表代号集中在这里，ViewModel 只按当前模块读取配置执行。
    /// </summary>
    private sealed record QuestionnaireDefinition(
        string ModuleCode,
        string QuestionnaireCode,
        string TitleKey,
        string SubtitleKey,
        string InstructionKey,
        QuestionnaireQuestion[] Questions,
        string[] AnswerOptions);

    /// <summary>
    /// 采集工作台模块定义。
    /// Code 用于业务判断、数据库记录和文件夹命名；DisplayNameKey 只负责界面显示。
    /// </summary>
    private sealed record CaptureWorkbenchModule(string Code, string DisplayNameKey, bool IsDevelopmentOnly = false);

    private const string EyeCalibrationModuleCode = "eye_calibration";
    private const string PictureBrowseModuleCode = "picture_browse";
    private const string VideoBrowseModuleCode = "video_browse";
    private const string VoiceBaselineModuleCode = "voice_baseline";
    private const string WordReadingModuleCode = "word_reading";
    private const string ShortTextReadingModuleCode = "short_text_reading";
    private const string EmotionQuestionModuleCode = "emotion_question";
    private const string DotProbeModuleCode = "dot_probe";
    private const string EmotionOddballModuleCode = "emotion_oddball";
    private const string EmotionLetterSearchModuleCode = "emotion_letter_search";
    private const string EmotionStroopModuleCode = "emotion_stroop";
    private const string BasicInfoModuleCode = "basic_info";
    private const string QuestionnaireAModuleCode = "questionnaire_a";
    private const string QuestionnaireBModuleCode = "questionnaire_b";
    private const string QuestionnaireCModuleCode = "questionnaire_c";
    private const string QuestionnaireDModuleCode = "questionnaire_d";
    private const string QuestionnaireEModuleCode = "questionnaire_e";
    private const string QuestionnaireFModuleCode = "questionnaire_f";
    private const string QuestionnaireGModuleCode = "questionnaire_g";
    private const string QuestionnaireHModuleCode = "questionnaire_h";
    private const string QuestionnaireIModuleCode = "questionnaire_i";
    private const string QuestionnaireJModuleCode = "questionnaire_j";
    private const string SyncTestModuleCode = "sync_test";

    /// <summary>
    /// 采集工作台统一强制休息时间。
    /// 图片浏览、视频浏览以及后续新增采集模块的休息阶段均固定 12 秒，不允许用户手动跳过。
    /// </summary>
    private const int CaptureWorkbenchForcedRestSeconds = 12;

    /// <summary>
    /// 视频浏览真实视频之间的固定间隔。
    /// 业务含义：上一段真实视频结束后，强制休息 12 秒，再空屏 2 秒，
    /// 因此下一段真实视频开始时间可按“上一段结束时间 + 14 秒”推断。
    /// 该规则用于后续把视频素材时间段与采集到的人脸画面、声音做时间轴对齐。
    /// </summary>
    private const int VideoBrowseBlankMilliseconds = 2000;

    private const int VideoBrowseRealVideoIntervalSeconds = CaptureWorkbenchForcedRestSeconds + (VideoBrowseBlankMilliseconds / 1000);

    /// <summary>
    /// 开发专用音画同步测试录制时长。
    /// 该模块只用于快速验证录制链路，后续正式交付时可通过配置隐藏。
    /// </summary>
    private const int SyncTestDurationSeconds = 60;

    private const int VoiceBaselineSegmentSeconds = 6;

    private const int WordReadingGroupSeconds = 15;

    private const int ShortTextReadingPassageSeconds = 30;

    private const int EmotionQuestionAnswerSeconds = 30;

    private static readonly CaptureWorkbenchModule[] CaptureWorkbenchModules =
    [
        new(EyeCalibrationModuleCode, "ModuleEyeCalibration"),
        new(PictureBrowseModuleCode, "ModulePictureBrowse"),
        new(VideoBrowseModuleCode, "ModuleVideoBrowse"),
        new(VoiceBaselineModuleCode, "ModuleVoiceBaseline"),
        new(WordReadingModuleCode, "ModuleWordReading"),
        new(ShortTextReadingModuleCode, "ModuleShortTextReading"),
        new(EmotionQuestionModuleCode, "ModuleEmotionQuestion"),
        new(DotProbeModuleCode, "ModuleDotProbe"),
        new(EmotionOddballModuleCode, "ModuleEmotionOddball"),
        new(EmotionLetterSearchModuleCode, "ModuleEmotionLetterSearch"),
        new(EmotionStroopModuleCode, "ModuleEmotionStroop"),
        new(BasicInfoModuleCode, "ModuleBasicInfo"),
        new(QuestionnaireAModuleCode, "ModuleQuestionnaireA"),
        new(QuestionnaireBModuleCode, "ModuleQuestionnaireB"),
        new(QuestionnaireCModuleCode, "ModuleQuestionnaireC"),
        new(QuestionnaireDModuleCode, "ModuleQuestionnaireD"),
        new(QuestionnaireEModuleCode, "ModuleQuestionnaireE"),
        new(QuestionnaireFModuleCode, "ModuleQuestionnaireF"),
        new(QuestionnaireGModuleCode, "ModuleQuestionnaireG"),
        new(QuestionnaireHModuleCode, "ModuleQuestionnaireH"),
        new(QuestionnaireIModuleCode, "ModuleQuestionnaireI"),
        new(QuestionnaireJModuleCode, "ModuleQuestionnaireJ"),
        new(SyncTestModuleCode, "ModuleSyncTest", true)
    ];

    /// <summary>
    /// 图片浏览第三步内部状态。
    /// 休息阶段不做人脸取景判定，固定休息 12 秒后自动继续。
    /// </summary>
    private enum PictureBrowsePhase
    {
        Idle,
        ShowingImage,
        Blank,
        Resting,
        Completed
    }

    /// <summary>
    /// 视频浏览第三步内部状态。
    /// 正式视频播放必须等待 MediaElement 自然结束，空屏和休息由流程状态控制。
    /// </summary>
    private enum VideoBrowsePhase
    {
        Idle,
        Blank,
        PlayingVideo,
        Resting,
        Completed
    }

    /// <summary>
    /// 语音基线第三步内部状态。
    /// 第一段需要用户点击开始，后续段落由 12 秒休息倒计时结束后自动开始。
    /// </summary>
    private enum VoiceBaselinePhase
    {
        Idle,
        WaitingToStart,
        Recording,
        Resting,
        Completed
    }

    /// <summary>
    /// 词语朗读第三步内部状态。
    /// 第一组需要用户点击开始，后续词组由 12 秒休息倒计时结束后自动开始。
    /// </summary>
    private enum WordReadingPhase
    {
        Idle,
        WaitingToStart,
        Reading,
        Resting,
        Completed
    }

    /// <summary>
    /// 短文朗读第三步内部状态。第一段手动开始，后续段落由固定休息倒计时自动推进。
    /// </summary>
    private enum ShortTextReadingPhase
    {
        Idle,
        WaitingToStart,
        Reading,
        Resting,
        Completed
    }

    /// <summary>
    /// 情绪问答第三步内部状态。第一题手动开始，后续问题由固定休息倒计时自动推进。
    /// </summary>
    private enum EmotionQuestionPhase
    {
        Idle,
        WaitingToStart,
        Answering,
        Resting,
        Completed
    }

    private static readonly VoiceBaselineItem[] VoiceBaselineItems =
    [
        new("请您连续发出“啊（a）”的声音", "啊", 1),
        new("请您连续发出“衣（yi）”的声音", "衣", 3),
        new("请您连续发出“哦（o）”的声音", "哦", 2)
    ];

    private static readonly WordReadingGroup[] WordReadingGroups =
    [
        new(["常规的", "金属的", "表面的", "对称的", "混合的", "平坦的"], 3),
        new(["温馨的", "幸福的", "欢快的", "坚强的", "乐观的", "美满的"], 1),
        new(["痛苦的", "疲惫的", "卑微的", "暴躁的", "无助的", "失败的"], 2),
        new(["昼夜交替", "由表及里", "就地取材", "平铺直叙", "按部就班", "南来北往"], 3),
        new(["万念俱灰", "家破人亡", "众叛亲离", "颠沛流离", "痛不欲生", "孤苦伶仃"], 2),
        new(["意气风发", "喜笑颜开", "旗开得胜", "精神抖擞", "心满意足", "兴高采烈"], 1)
    ];

    private static readonly ShortTextReadingPassage[] ShortTextReadingPassages =
    [
        new(
            "选取一百五十克绿豆，用清水淘洗，剔除浮沫与坏豆；在容器内铺设无菌纱布，倒入浸泡八小时的豆粒；每日早晚各进行一次淋水操作，水量需完全浸透布料又不过度积水；将容器放在避光处，环境温度维持在二十五摄氏度，五至七天后即可采收豆芽。",
            3),
        new(
            "夜深十二点半，客厅再成修罗场，父亲摔杯砸碗，母亲尖声咒骂；争吵的声音击碎他仅剩的一点倦意，他蜷缩在被窝里，数着心跳对抗着噪音；他早已记不清这是他们多少次干架了，乌烟瘴气的家里总是争吵不休，弱小的他无奈又无助，只有默默地流泪。",
            2),
        new(
            "阳光明媚的春日午后，她和家人朋友们围坐在户外的野餐垫上，分享着各自带来的美食，讲述着生活中的趣事。时而传出阵阵欢声笑语；轻松愉快的氛围让她倍感欢乐和温暖，看着身边这些亲密的家人和朋友，她感到自己的生活是如此的美好和幸福。",
            1)
    ];

    private static readonly EmotionQuestionPrompt[] EmotionQuestionPrompts =
    [
        new(
            "问题1：你最近的心情怎么样？请你讲一两件最近让你感到开心或者不开心的事情，说说是什么事情？怎么发生的？你是什么样的感受？讲得越详细越好。",
            3),
        new(
            "问题2：请你仔细想一想，讲述一两件与你相关或你遇到过的，让你感到特别有能量、非常开心、幸福、得意或满意的事情，讲得越详细越好。",
            1),
        new(
            "问题3：请你讲述一两件与你相关或你遇到过的，让你感到非常难过、伤心、烦躁、苦恼或者痛苦的事情，说说是什么事情？怎么发生的？当时你的感受如何？讲得越详细越好。",
            2)
    ];

    private static readonly string[] BasicInfoGenderOptions = ["男", "女"];

    private static readonly string[] BasicInfoEducationOptions =
    [
        "小学及以下",
        "初中",
        "高中/中专/技校",
        "大专",
        "本科",
        "硕士研究生",
        "博士研究生"
    ];

    private static readonly string[] BasicInfoOccupationOptions =
    [
        "国家机关、党群组织、企事业单位负责人",
        "专业技术人员",
        "办事人员和有关人员",
        "商业、服务业人员",
        "农、林、牧、渔、水利业生产人员",
        "生产、运输设备操作人员",
        "军人",
        "不便分类的其他从业人员",
        "其他"
    ];

    private static readonly string[] BasicInfoIncomeOptions =
    [
        "3000元/月及以下",
        "3001~6000元/月",
        "6001~9000元/月",
        "9001~12000元/月",
        "12001~15000元/月",
        "15001元/月及以上"
    ];

    private static readonly QuestionnaireQuestion[] QuestionnaireAQuestions =
    [
        new(1, "感觉紧张，焦虑或急切"),
        new(2, "不能够停止或控制担忧"),
        new(3, "对各种各样的事情担忧过多"),
        new(4, "很难放松下来"),
        new(5, "由于不安而无法静坐"),
        new(6, "变得很容易烦恼或急躁"),
        new(7, "感到似乎有可怕的事情发生而害怕")
    ];

    private static readonly QuestionnaireQuestion[] QuestionnaireBQuestions =
    [
        new(1, "做事时提不起劲或没有兴趣"),
        new(2, "感到心情低落，沮丧或绝望"),
        new(3, "入睡困难、睡不安稳或睡眠过多"),
        new(4, "感觉疲倦或没有活力"),
        new(5, "食欲不振或吃的太多"),
        new(6, "觉得自己很糟糕，或觉得自己很失败，或让自己和家人失望"),
        new(7, "很难集中注意力去做事情，比如读书、看报或者看电视"),
        new(8, "动作或说话速度缓慢到别人已经察觉或正好相反，烦躁或坐立不安，动来动去的情况比平时多"),
        new(9, "有不如死掉或用某种方式伤害自己的念头")
    ];

    private static readonly QuestionnaireQuestion[] QuestionnaireCQuestions =
    [
        new(1, "我喜欢看电视、听广播或音乐"),
        new(2, "和家人或亲密的朋友在一起我感到很快乐"),
        new(3, "我能从平常的爱好和消遣中得到乐趣"),
        new(4, "我仍然喜欢我平常的食物"),
        new(5, "我能从热水澡或淋浴中得到放松"),
        new(6, "闻到花香、或者清新的海风或者新鲜出炉的面包我会觉得高兴"),
        new(7, "我喜欢看到他人的微笑"),
        new(8, "我欣赏自己的穿着打扮"),
        new(9, "我仍然喜欢阅读书籍、杂志或报纸"),
        new(10, "我仍然喜欢喝咖啡、茶、或其它饮料"),
        new(11, "我喜欢从小处发现生活的乐趣，例如明亮的阳光、朋友的电话问候"),
        new(12, "我喜欢外面亮丽的风景"),
        new(13, "我能够助人为乐"),
        new(14, "当别人赞美我时，我会感到很愉悦")
    ];

    private static readonly QuestionnaireQuestion[] QuestionnaireDQuestions =
    [
        new(1, "入睡时间延迟（关灯后到入睡时间）", ["没问题", "轻微延迟", "显著延迟", "严重延迟或没有睡觉"]),
        new(2, "夜间觉醒", ["没问题", "轻微影响", "显著影响", "严重影响或没有睡觉"]),
        new(3, "比期望的时间早醒", ["没问题", "轻微提早", "显著提早", "严重提早或没有睡觉"]),
        new(4, "总睡眠时间不足", ["足够", "轻微不足", "显著不足", "严重不足或没有睡觉"]),
        new(5, "总睡眠质量不满（无论睡多久）", ["满意", "轻微不满", "显著不满", "严重不满或没有睡觉"]),
        new(6, "白天情绪低落", ["正常", "轻微低落", "显著低落", "严重低落"]),
        new(7, "白天身体功能受影响（体力或精神：如记忆力、认知力和注意力）", ["足够", "轻微影响", "显著影响", "严重影响"]),
        new(8, "白天感到困倦想睡觉", ["无思睡", "轻微思睡", "显著思睡", "严重思睡"])
    ];

    private static readonly string[] DietFrequencyOptions = ["每天都吃", "每周 3-5 次", "每周 1-2 次", "不吃或很少吃"];

    private static readonly string[] OilFrequencyOptions =
    [
        "从不吃",
        "每月少于 1 次",
        "每月 1-3 次",
        "每周 1-2 次",
        "每周 3-4 次",
        "每周 5-6 次",
        "每天 1 次",
        "每天 2 次",
        "每天 3 次及以上"
    ];

    private static readonly QuestionnaireQuestion[] QuestionnaireEQuestions =
    [
        new(1, "您平时饮食的冷热度", ["热、烫", "适中", "凉"]),
        new(2, "您平时饮食的口味", ["重盐", "适中", "清淡"]),
        new(3, "您平时食用菜肴或食物的油脂含量", ["高", "适中", "低"]),
        new(4, "食用含糖食物(含菜肴)的频率", DietFrequencyOptions),
        new(5, "喝含糖饮料的频率", ["每天都吃", "每周 3-5 次", "每周 1-2 次", "不喝或很少喝"]),
        new(6, "您平时饮食的辣味程度", ["重辣", "中辣", "微辣", "不吃辣"]),
        new(7, "您进食的速度", ["快(<15 分钟)", "适中（15-30 分钟）", "慢（>30 分钟）"]),
        new(8, "您的饮食规律", ["三餐规律", "有时不规律", "不规律"]),
        new(9, "食用剩饭、剩菜(含隔顿和隔夜剩饭菜)的频率", DietFrequencyOptions),
        new(10, "您的饮食偏好", ["喜欢肉食", "荤素平衡", "喜欢素食"]),
        new(11, "食用畜类肉（猪、牛、羊肉等）或禽类肉（鸡、鸭、鹅肉等）的频率", DietFrequencyOptions),
        new(12, "食用加工肉（腊肉、香肠、培根、丸子等）的频率", DietFrequencyOptions),
        new(13, "食用蛋类（鸡蛋、鸭蛋等）的频率", DietFrequencyOptions),
        new(14, "食用细粮（精米、面粉制品）的频率", DietFrequencyOptions),
        new(15, "吃全谷物或杂粮的频率", DietFrequencyOptions),
        new(16, "食用蔬菜的频率", DietFrequencyOptions),
        new(17, "食用新鲜水果的频率", DietFrequencyOptions),
        new(18, "食用腌制食品（咸菜、梅干菜、榨菜、泡菜等）的频率", DietFrequencyOptions),
        new(19, "食用乳类食物（牛奶、酸奶、奶粉等）的频率", DietFrequencyOptions),
        new(20, "食用豆类及豆制品（豆腐、豆浆、豆干等）的频率", DietFrequencyOptions),
        new(21, "食用水产品（鱼、虾等）的频率", DietFrequencyOptions),
        new(22, "吃早餐的频率", DietFrequencyOptions),
        new(23, "吃宵夜的频率", DietFrequencyOptions),
        new(24, "您是素食者吗", ["否", "是，完全素食者（不吃肉、奶、蛋）", "蛋奶素食者（不吃肉，吃奶、蛋）", "奶素食者（不吃肉、蛋，饮用奶）"]),
        new(25, "您平时吃饭吃到什么程度", ["过饱(>10 分)", "适中(7-9 分)", "偏少(≤6 分)"]),
        new(26, "您平时的晚餐结束时间", ["20:00 以前", "20:00 以后", "不吃晚餐"]),
        new(27, "您平时饮食的干稀度", ["干", "适中", "稀"]),
        new(28, "您平时饮食的软硬度", ["硬", "适中", "软"]),
        new(29, "您平时在外就餐的频率", DietFrequencyOptions),
        new(30, "您平时吃外卖的频率", DietFrequencyOptions),
        new(31, "食用内脏类（肝/肾/血/舌/大肠/肚/肺等）食物的频率", DietFrequencyOptions),
        new(32, "食用植物油脂的频率（大豆油/花生油/橄榄油/山茶油等）", OilFrequencyOptions),
        new(33, "食用动物油脂的频率(猪油/黄油/奶油/牛油等)", OilFrequencyOptions),
        new(34, "您是否经常服用膳食补充剂（每周 3 次以上）", ["是", "否"]),
        new(35, "您服用膳食补充剂的种类", ["复合维生素（如善存）", "钙", "铁剂", "维生素C", "B族维生素", "其他"])
    ];

    private static readonly QuestionnaireQuestion[] QuestionnaireFQuestions =
    [
        new(1, "在过去的一个月里，你有多少时间因为发生意外的事情而感到心烦意乱？"),
        new(2, "在过去的一个月里，有多少时间你感到无法掌控生活中重要的事情？"),
        new(3, "在过去的一个月里，有多少时间你感觉到神经紧张或“快被压垮了”？"),
        new(4, "在过去的一个月里，有多少时间你对自己处理个人问题的能力感到有信心？"),
        new(5, "在过去的一个月里，有多少时间你感到事情发展和你预料的一样？"),
        new(6, "在过去的一个月里，有多少时间你发现自己无法应付那些你必须去做的事情？"),
        new(7, "在过去的一个月里，日常生活中有多少时间你能够控制自己的愤怒情绪？"),
        new(8, "在过去的一个月里，有多少时间你感到处理事情得心应手（事情都在你的控制之中）？"),
        new(9, "在过去的一个月里，有多少时间你因为一些超出自己控制能力的事情而感到愤怒？"),
        new(10, "在过去的一个月里，有多少时间你感到问题堆积如山，已经无法逾越？")
    ];

    private static readonly QuestionnaireQuestion[] QuestionnaireGQuestions =
    [
        new(1, "当我想感受一些积极的情绪（如快乐或高兴）时，我会改变自己思考问题的角度。"),
        new(2, "我不会表露自己的情绪。"),
        new(3, "当我想少感受一些消极的情绪(如悲伤或愤怒)时，我会改变自己思考问题的角度。"),
        new(4, "当感受到积极情绪时，我会很小心地不让它们表露出来。"),
        new(5, "在面对压力情境时，我会使自己以一种有助于保持平静的方式来考虑它。"),
        new(6, "我控制自己情绪的方式是不表达它们。"),
        new(7, "当我想多感受一些积极的情绪时，我会改变自己对情境的考虑方式。"),
        new(8, "我会通过改变对情境的考虑方式来控制自己的情绪。"),
        new(9, "当感受到消极的情绪时，我确定不会表露它们。"),
        new(10, "当我想少感受一些消极的情绪时，我会改变自己对情境的考虑方式。")
    ];

    private static readonly QuestionnaireQuestion[] QuestionnaireHQuestions =
    [
        new(1, "被人误会或错怪"),
        new(2, "受人歧视冷遇"),
        new(3, "考试/任务失败或不理想"),
        new(4, "与同学/好友发生纠纷"),
        new(5, "生活习惯（饮食、休息等）明显变化"),
        new(6, "不喜欢上学/上班/工作"),
        new(7, "恋爱不顺利或失恋"),
        new(8, "长期远离家人不能团聚"),
        new(9, "学习/工作负担重"),
        new(10, "与老师/领导关系紧张"),
        new(11, "本人患急重病"),
        new(12, "亲友患急重病"),
        new(13, "亲友死亡"),
        new(14, "被盗或丢失东西"),
        new(15, "当众丢面子"),
        new(16, "家庭/个人经济困难、收入减少、负债"),
        new(17, "家庭内部有矛盾"),
        new(18, "预期的选优或评奖（如奖金）落空"),
        new(19, "受批评或处分"),
        new(20, "转学/休学或换工作"),
        new(21, "被罚款"),
        new(22, "升学/升职压力"),
        new(23, "与人打架或遭受暴力"),
        new(24, "遭父母/家人打骂"),
        new(25, "家庭给你施加压力"),
        new(26, "意外惊吓、意外事故"),
        new(27, "分居/离婚或家庭变故"),
        new(28, "失业/退休或辍学/退学"),
        new(29, "遭受重大自然灾害"),
        new(30, "其他事件")
    ];

    private static readonly QuestionnaireQuestion[] QuestionnaireJQuestions =
    [
        new(1, "在大多数方面，我的生活接近理想"),
        new(2, "我的生活条件非常好"),
        new(3, "我对生活感到满意"),
        new(4, "到目前为止，我已经得到了生活中想要的重要东西"),
        new(5, "如果我可以重新活一次，我几乎不会改变任何东西")
    ];

    private static readonly string[] QuestionnaireCommonAnswerOptions =
    [
        "完全不会",
        "好几天",
        "超过一周",
        "几乎每天"
    ];

    private static readonly string[] QuestionnaireAgreementAnswerOptions =
    [
        "非常同意",
        "同意",
        "不同意",
        "非常不同意"
    ];

    private static readonly string[] QuestionnaireStressAnswerOptions =
    [
        "从未有",
        "几乎没有",
        "偶尔",
        "经常",
        "非常多"
    ];

    private static readonly string[] QuestionnaireEmotionRegulationAnswerOptions =
    [
        "完全不同意",
        "很不同意",
        "有点不同意",
        "中性",
        "有点同意",
        "很同意",
        "完全同意"
    ];

    private static readonly string[] QuestionnaireLifeEventAnswerOptions =
    [
        "未发生",
        "发生过，无影响",
        "发生过，轻度影响",
        "发生过，中度影响",
        "发生过，重度影响",
        "发生过，极重影响"
    ];

    private static readonly string[] QuestionnaireZeroToTenAnswerOptions =
    [
        "0",
        "1",
        "2",
        "3",
        "4",
        "5",
        "6",
        "7",
        "8",
        "9",
        "10"
    ];

    private static readonly string[] QuestionnaireYesNoAnswerOptions =
    [
        "是",
        "否"
    ];

    private static readonly string[] QuestionnaireLivingAreaAnswerOptions =
    [
        "小于5平米",
        "6~12平米",
        "13~19平米",
        "20~26平米",
        "27~33平米",
        "34~40平米",
        "41平米以上"
    ];

    private static readonly string[] QuestionnaireWorryAnswerOptions =
    [
        "不担心",
        "有点担心或非常担心"
    ];

    private static readonly QuestionnaireQuestion[] QuestionnaireIQuestions =
    [
        new(1, "您所生活或居住的地方的社会或社交关系对您的支持和帮助程度有多大？（从0~10分中打分）", QuestionnaireZeroToTenAnswerOptions),
        new(2, "您对您所在或生活的地方（小区/村庄）的满意程度有多大？（从0~10分中打分）", QuestionnaireZeroToTenAnswerOptions),
        new(3, "您所生活或居住空间的人均面积有多大？", QuestionnaireLivingAreaAnswerOptions),
        new(4, "您对您所居住房子的满意程度？（从0~10分中打分）", QuestionnaireZeroToTenAnswerOptions),
        new(5, "您对自己所居住的地方的归属感程度？（从0~10分中打分）", QuestionnaireZeroToTenAnswerOptions),
        new(6, "您所居住的地方是否有比较大的噪音（如邻居的噪音或外界交通、工业设施的噪音）？", QuestionnaireYesNoAnswerOptions),
        new(7, "您所居住的地方是否有比较大的灰尘、气味或其他污染问题？", QuestionnaireYesNoAnswerOptions),
        new(8, "在您所居住的地方200米范围内是否有可供玩耍和娱乐的区域？", QuestionnaireYesNoAnswerOptions),
        new(9, "在您所居住的地方500米范围内是否有可供徒步旅行的地形或地方？", QuestionnaireYesNoAnswerOptions),
        new(10, "当您在您所居住的地方散步或活动时，您觉得所处环境的安全程度是？（从0~10分中打分）", QuestionnaireZeroToTenAnswerOptions),
        new(11, "您所居住的地方是否有犯罪、暴力或故意破坏的问题？", QuestionnaireYesNoAnswerOptions),
        new(12, "您最近独自外出时，是否担心在居住地附近遭遇暴力或人身威胁？", QuestionnaireWorryAnswerOptions),
        new(13, "您觉得与您一样的人对地方政府的所作所为的影响程度有多大？（从0~10分中打分）", QuestionnaireZeroToTenAnswerOptions),
        new(14, "在过去的一年里，您是否因为年龄、性别、健康/疾病问题、残疾、种族背景、肤色、宗教、政治态度、性身份等原因遭受了比其他人更糟糕或更差的待遇？", QuestionnaireYesNoAnswerOptions),
        new(15, "如果您生病、受伤或无法工作，您觉得政府或福利部门会给您提供帮助的程度有多大？（从0~10分中打分）", QuestionnaireZeroToTenAnswerOptions)
    ];

    private static readonly QuestionnaireDefinition[] QuestionnaireDefinitions =
    [
        new(
            QuestionnaireAModuleCode,
            "GAD-7",
            "ModuleQuestionnaireA",
            "CaptureWorkspaceQuestionnaireASubtitle",
            "CaptureWorkspaceQuestionnaireInstruction",
            QuestionnaireAQuestions,
            QuestionnaireCommonAnswerOptions),
        new(
            QuestionnaireBModuleCode,
            "PHQ-9",
            "ModuleQuestionnaireB",
            "CaptureWorkspaceQuestionnaireBSubtitle",
            "CaptureWorkspaceQuestionnaireInstruction",
            QuestionnaireBQuestions,
            QuestionnaireCommonAnswerOptions),
        new(
            QuestionnaireCModuleCode,
            "SHAPS",
            "ModuleQuestionnaireC",
            "CaptureWorkspaceQuestionnaireCSubtitle",
            "CaptureWorkspaceQuestionnaireCInstruction",
            QuestionnaireCQuestions,
            QuestionnaireAgreementAnswerOptions),
        new(
            QuestionnaireDModuleCode,
            "AIS",
            "ModuleQuestionnaireD",
            "CaptureWorkspaceQuestionnaireDSubtitle",
            "CaptureWorkspaceQuestionnaireDInstruction",
            QuestionnaireDQuestions,
            []),
        new(
            QuestionnaireEModuleCode,
            "Dietary",
            "ModuleQuestionnaireE",
            "CaptureWorkspaceQuestionnaireESubtitle",
            "CaptureWorkspaceQuestionnaireEInstruction",
            QuestionnaireEQuestions,
            []),
        new(
            QuestionnaireFModuleCode,
            "PSS",
            "ModuleQuestionnaireF",
            "CaptureWorkspaceQuestionnaireFSubtitle",
            "CaptureWorkspaceQuestionnaireFInstruction",
            QuestionnaireFQuestions,
            QuestionnaireStressAnswerOptions),
        new(
            QuestionnaireGModuleCode,
            "ERQ",
            "ModuleQuestionnaireG",
            "CaptureWorkspaceQuestionnaireGSubtitle",
            "CaptureWorkspaceQuestionnaireGInstruction",
            QuestionnaireGQuestions,
            QuestionnaireEmotionRegulationAnswerOptions),
        new(
            QuestionnaireHModuleCode,
            "SLEC",
            "ModuleQuestionnaireH",
            "CaptureWorkspaceQuestionnaireHSubtitle",
            "CaptureWorkspaceQuestionnaireHInstruction",
            QuestionnaireHQuestions,
            QuestionnaireLifeEventAnswerOptions),
        new(
            QuestionnaireIModuleCode,
            "LEQ-0-10",
            "ModuleQuestionnaireI",
            "CaptureWorkspaceQuestionnaireISubtitle",
            "CaptureWorkspaceQuestionnaireIInstruction",
            QuestionnaireIQuestions,
            []),
        new(
            QuestionnaireJModuleCode,
            "SWLS",
            "ModuleQuestionnaireJ",
            "CaptureWorkspaceQuestionnaireJSubtitle",
            "CaptureWorkspaceQuestionnaireJInstruction",
            QuestionnaireJQuestions,
            QuestionnaireEmotionRegulationAnswerOptions)
    ];

    private static QuestionnaireDefinition? GetQuestionnaireDefinition(string moduleCode)
    {
        return QuestionnaireDefinitions.FirstOrDefault(definition =>
            string.Equals(definition.ModuleCode, moduleCode, StringComparison.OrdinalIgnoreCase));
    }

    private readonly string[] devSteps =
    [
        "准备检查",
        "演示视频",
        "面部取景",
        "模块执行",
        "模块完成"
    ];

    private readonly CalibrationTrial[] calibrationTrials =
    [
        new(8, 1200, 1000, 800, [1, 5, 3, 7, 8, 4, 6, 2], true),
        new(10, 800, 1000, 800, [1, 2, 2, 1, 2, 2, 1, 1, 2, 1], false),
        new(8, 800, 1000, 800, [4, 6, 1, 8, 7, 2, 5, 3], true),
        new(10, 800, 1000, 1200, [2, 1, 1, 2, 2, 2, 1, 1, 1, 2], false)
    ];
}
