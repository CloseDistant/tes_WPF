# OpenVINO CPU 运行库

本目录随软件部署 OpenVINO 2026.2.0 的 Windows x64 最小 CPU 运行时，用于项目内的 IR 人脸检测、关键点和头部姿态模型。

仅保留当前运行路径需要的 OpenVINO 核心、C API、CPU 插件、IR 前端和 TBB 依赖；不包含未使用的 GPU、NPU、ONNX、TensorFlow、TensorFlow Lite、PyTorch、Paddle、AUTO、HETERO 和自动批处理插件。

- 上游项目：OpenVINO
- 上游版本：2026.2.0
- 来源包：`Sdcb.OpenVINO.runtime.win-x64` 2026.2.0
- 许可证：Apache License 2.0，全文见 `LICENSE.txt`
- 运行平台：Windows x64，CPU

更新这些二进制文件时，必须使用同版本的官方运行库，并重新执行真实模型加载测试、完整构建和自动化测试。
