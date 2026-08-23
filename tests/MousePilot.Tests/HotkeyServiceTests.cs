using MousePilot.Services;

namespace MousePilot.Tests;

public class HotkeyServiceTests
{
    private sealed class FakeRegistrar
    {
        public List<(int Id, uint Mod, uint Vk)> Registered { get; } = new();
        public List<int> Unregistered { get; } = new();
        public bool NextResult = true;

        public bool Register(int id, uint mod, uint vk)
        {
            if (!NextResult)
            {
                return false;
            }

            Registered.Add((id, mod, vk));
            return true;
        }

        public bool Unregister(int id)
        {
            Unregistered.Add(id);
            return true;
        }
    }

    private static readonly HotkeyCombo Combo = new(HotkeyParser.ModControl | HotkeyParser.ModAlt, 0x78);

    [Fact]
    public void 註冊成功記錄組合()
    {
        var fake = new FakeRegistrar();
        using var service = new HotkeyService(fake.Register, fake.Unregister);
        Assert.True(service.Register(1, Combo));
        Assert.Equal((1, Combo.Modifiers, Combo.VirtualKey), fake.Registered.Single());
    }

    [Fact]
    public void 註冊失敗回傳false()
    {
        var fake = new FakeRegistrar { NextResult = false };
        using var service = new HotkeyService(fake.Register, fake.Unregister);
        Assert.False(service.Register(1, Combo));
    }

    [Fact]
    public void 同id重註冊會先解除舊的()
    {
        var fake = new FakeRegistrar();
        using var service = new HotkeyService(fake.Register, fake.Unregister);
        service.Register(1, Combo);
        service.Register(1, new HotkeyCombo(HotkeyParser.ModControl, 0x41));
        Assert.Equal(new[] { 1 }, fake.Unregistered);
        Assert.Equal(2, fake.Registered.Count);
    }

    [Fact]
    public void Dispose解除全部註冊()
    {
        var fake = new FakeRegistrar();
        var service = new HotkeyService(fake.Register, fake.Unregister);
        service.Register(1, Combo);
        service.Register(2, new HotkeyCombo(HotkeyParser.ModControl, 0x41));
        service.Dispose();
        Assert.Equal(new[] { 1, 2 }, fake.Unregistered.OrderBy(x => x));
    }

    [Fact]
    public void 註冊失敗記錄Win32錯誤碼()
    {
        var svc = new HotkeyService(registerFn: (_, _, _) => false, unregisterFn: _ => true, lastErrorFn: () => 1409);
        Assert.False(svc.Register(1, HotkeyParser.Parse("Ctrl+Alt+F9")!.Value));
        Assert.Equal(1409, svc.LastWin32Error);
    }

    [Fact]
    public void SimulatePress觸發事件()
    {
        var fake = new FakeRegistrar();
        using var service = new HotkeyService(fake.Register, fake.Unregister);
        var pressed = new List<int>();
        service.HotkeyPressed += pressed.Add;
        service.SimulatePress(2);
        Assert.Equal(new[] { 2 }, pressed);
    }
}
