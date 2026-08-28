# 坐骑速度写入功能实现说明

## 功能概述

本实现为坐骑速度修改功能提供了完整的框架架构，支持从**读取验证**到**实际写入**的完整流程。

## 已实现内容

### 1. 接口定义 (`IMountSpeedAdapter.cs`)
- `IMountSpeedReadOnlyAdapter` - 只读适配器接口
- `IMountSpeedOperationAdapter` - 操作适配器接口  
- `MountSpeedSnapshot` - 坐骑速度数据快照
- `OperationResult` - 操作结果枚举

### 2. 预设模型 (`MountSpeedPreset.cs`)
- `BaseSpeed` - 基础移动速度
- `ExpectedRideAddSpeed` - 期望的坐骑速度加成（百分比）
- `DesiredMount` - 是否希望乘坐坐骑
- `ApplyMountSpeed` - 是否应用坐骑速度修改
- `EnableStepLog` - 步骤日志启用

### 3. 状态机步骤 (`MountSpeedPresetPlan.cs`)
新增步骤：
- `APPLY_MOUNT_SPEED` - 应用坐骑速度步骤
- 总计 11 个步骤（原 10 个 + 新增的写入步骤）

### 4. 状态机运行器 (`MountSpeedPresetRunner.cs`)
- 支持注入操作适配器（可选）
- 实现写入步骤的调用逻辑
- 自动处理坐骑状态切换

### 5. 测试适配器 (`TestAdapters.cs`)
- `ScriptableTestReadOnlyAdapter` - 可配置的只读适配器
- `ScriptableTestOperationAdapter` - 可配置的写入适配器

### 6. 内存写入器 (`MountSpeedMemoryWriter.cs`)
- `MountSpeedMemoryWriter` - 坐骑速度内存写入器
- `WritableProcessMemoryHelper` - 进程内存写入帮助类
- 支持多种数据类型（Float/Double/Int32）
- 支持坐骑状态写入

### 7. 操作适配器 (`MountSpeedOperationAdapter.cs`)
- `MountSpeedOperationAdapter` - 主适配器实现
- `IMountSpeedMemoryWriter` - 内存写入器接口
- `IMountSpeedPacketInjector` - 封包注入器接口

## 使用方法

### 方式一：内存写入（需要内存地址）

```csharp
// 1. 创建内存写入器
var memoryWriter = new MountSpeedMemoryWriter(processIdentity);

// 2. 设置坐骑速度内存地址（需要用户通过 Cheat Engine 等工具发现）
memoryWriter.SetMemoryAddresses(
    rideAddSpeedAddress: 0x12345678,  // 坐骑速度加成地址
    isMountedAddress: 0x1234567C,       // 坐骑状态地址（可选）
    roleMoveSpeedAddress: 0x12345680  // 角色移动速度地址（可选）
);

// 3. 创建写入适配器
var operationAdapter = new MountSpeedOperationAdapter(memoryWriter);

// 4. 创建并运行预设
var preset = new MountSpeedPreset
{
    BaseSpeed = 200,
    ExpectedRideAddSpeed = 100,  // 100% 坐骑速度加成
    ApplyMountSpeed = true,
    DesiredMount = true
};

var runner = new MountSpeedPresetRunner(preset, readOnlyAdapter, operationAdapter);
await runner.StartAsync();
```

### 方式二：封包注入（需要封包模板）

```csharp
// 实现 IMountSpeedPacketInjector 接口
public class MyPacketInjector : IMountSpeedPacketInjector
{
    public Task<OperationResult> SetRideAddSpeedAsync(double rideAddSpeed, CancellationToken ct)
    {
        // 发送修改坐骑速度的封包
        // 需要知道游戏的坐骑速度协议
    }
    
    public Task<OperationResult> ToggleMountAsync(bool mount, CancellationToken ct)
    {
        // 发送乘坐/下马的封包
    }
}

// 使用封包注入器
var operationAdapter = new MountSpeedOperationAdapter(packetInjector);
```

## 内存地址获取方法

坐骑速度的内存地址需要通过以下方法获取：

### 方法 1：Cheat Engine 扫描
1. 启动游戏和 Cheat Engine
2. 附加到游戏进程
3. 创建新的扫描，搜索 `NaN` 或 `Float` 类型的值
4. 进行 "加速"（购买坐骑）后筛选结果，找出改变的地址
5. 记录坐骑速度加成的内存地址

### 方法 2：游戏协议分析
1. 捕获游戏发送的坐骑相关封包
2. 分析封包中包含的速度数据字段
3. 通过封包注入方式修改速度值

## 已知限制

1. **地址依赖**：实际写入需要知道坐骑速度在游戏内存中的具体地址
2. **游戏检测**：部分游戏有反外挂机制，内存写入可能触发警告
3. **协议变更**：游戏更新后可能需要重新获取地址
4. **写入时机**：需要在正确的游戏状态下写入，否则可能失败

## 后续开发建议

1. **完善封包注入**：实现基于协议分析的封包修改
2. **自动化地址扫描**：开发内存签名扫描功能
3. **速度同步**：实时监控并自动修正速度值
4. **多游戏支持**：抽象出通用的速度修改框架