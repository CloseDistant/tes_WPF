namespace RuinaoSoftwareWpf;

internal static class AuditDisplayNames
{
    public static string Category(AuditEventCategory category)
    {
        return category switch
        {
            AuditEventCategory.IdentitySession => "身份与会话",
            AuditEventCategory.AccountAuthorization => "账号与权限",
            AuditEventCategory.PatientManagement => "患者管理",
            AuditEventCategory.PrescriptionManagement => "处方管理",
            AuditEventCategory.StimulationDevice => "电刺激与设备",
            AuditEventCategory.SecurityConfiguration => "安全配置",
            AuditEventCategory.DataExport => "数据与导出",
            AuditEventCategory.AuditSystem => "审计系统",
            AuditEventCategory.IntegrityCheck => "完整性校验",
            _ => "未知类型"
        };
    }

    public static string Result(AuditEventResult result)
    {
        return result switch
        {
            AuditEventResult.Success => "成功",
            AuditEventResult.Blocked => "已阻止",
            _ => "失败"
        };
    }

    public static string Role(int? roleId)
    {
        return roleId switch
        {
            AccountRoles.Admin => "管理员",
            AccountRoles.Doctor => "医师",
            AccountRoles.Technician => "技术员",
            _ => "系统"
        };
    }

    public static string Action(string actionCode)
    {
        return actionCode switch
        {
            "LOGIN" => "登录",
            "LOGOUT" => "退出登录",
            "SWITCH_ACCOUNT" => "切换账号",
            "SESSION_TIMEOUT" => "会话超时",
            "SESSION_LOCK" => "会话锁定",
            "SESSION_UNLOCK" => "会话解锁",
            "CREATE_USER" => "创建账号",
            "VIEW_ACCOUNT_LIST" => "查看账号列表",
            "CHANGE_PASSWORD" => "修改密码",
            "FORCE_CHANGE_PASSWORD" => "首次修改密码",
            "RESET_PASSWORD" => "重置密码",
            "SOFTWARE_ACTIVATION" => "软件激活",
            "CREATE_PRESCRIPTION" => "新建处方",
            "UPDATE_PRESCRIPTION" => "修改处方",
            "COPY_PRESCRIPTION" => "复制处方",
            "DELETE_PRESCRIPTION" => "删除处方",
            "USE_PRESCRIPTION" => "调用处方",
            "REUSE_TREATMENT_RECORD" => "复用历史参数",
            "CREATE_PATIENT" => "新增患者",
            "UPDATE_PATIENT" => "修改患者",
            "SWITCH_PATIENT" => "切换患者",
            "UPDATE_FEATURE_VISIBILITY" => "修改功能显示",
            "UPDATE_STARTUP_SETTINGS" => "修改启动设置",
            "UPDATE_SESSION_SECURITY_SETTINGS" => "修改会话安全设置",
            "DEVICE_CONNECT" => "设备联机",
            "DEVICE_DISCONNECT" => "设备断开",
            "HANDSHAKE_CHECK" => "握手检测",
            "IMPEDANCE_CHECK" => "阻抗检测",
            "STIMULATION_START" => "启动直流电刺激",
            "STIMULATION_STOP" => "停止直流电刺激",
            "EMERGENCY_STOP" => "紧急停止",
            "AUDIT_QUERY" => "审计查询",
            "AUDIT_VERIFY" => "完整性校验",
            "AUDIT_DATABASE_INIT" => "审计库初始化",
            "EXPORT_AUDIT_CSV" => "导出审计CSV",
            "EXPORT_PRESCRIPTION_CSV" => "导出处方CSV",
            "EXPORT_TREATMENT_RECORD_CSV" => "导出治疗记录CSV",
            "DATA_INTEGRITY_FAILURE" => "数据完整性异常",
            "RELEASE_INTEGRITY_CHECK" => "发布文件校验",
            "DATA_INTEGRITY_CHECK" => "数据校验",
            "DATA_BACKUP_CREATE" => "创建数据备份",
            _ => actionCode
        };
    }
}
