# tES libusbK驱动说明

工程师软件连接当前业务固件设备：`USB\VID_04B4&PID_00F1`。

- Windows驱动服务必须为`libusbK`。
- 工程师软件通过共享模块`RuinaoTesHardware`调用`libusbK.dll`。
- `04B4:00F3 / WestBridge`是FX3 Bootloader身份，不作为正常业务联机目标。
- 当前InfWizard生成的自签名驱动仅用于内部联调；正式安装包必须包含正式签名的INF、CAT和`libusbK.sys`。
- 最终安装程序应使用Windows PnPUtil或驱动安装API安装驱动，不依赖InfWizard或已弃用的DPInst流程。

当前设备接口包含两个Bulk端点：OUT `0x01`、IN `0x81`。软件运行时仍会从USB描述符自动枚举端点，不写死端点地址。
