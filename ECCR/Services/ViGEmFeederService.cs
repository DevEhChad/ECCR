using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace ECCR.Services;

public class ViGEmFeederService : IDisposable
{
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private readonly object _lock = new();

    public bool IsInitialized => _controller != null;

    public bool Initialize()
    {
        lock (_lock)
        {
            if (_controller != null) return true;

            try
            {
                _client = new ViGEmClient();
                _controller = _client.CreateXbox360Controller();
                _controller.Connect();
                return true;
            }
            catch
            {
                _controller = null;
                _client = null;
                return false;
            }
        }
    }

    public void DispatchButton(string targetOutput, bool isPressed)
    {
        if (_controller == null && !Initialize()) return;
        if (_controller == null) return;

        string target = targetOutput.ToUpperInvariant();

        Xbox360Button? button = null;

        if (target.Contains("XBOX A") || target.Contains("XBOX-A") || target.Contains("CROSS / SOUTH"))
            button = Xbox360Button.A;
        else if (target.Contains("XBOX B") || target.Contains("XBOX-B") || target.Contains("CIRCLE / EAST"))
            button = Xbox360Button.B;
        else if (target.Contains("XBOX X") || target.Contains("XBOX-X") || target.Contains("SQUARE / WEST"))
            button = Xbox360Button.X;
        else if (target.Contains("XBOX Y") || target.Contains("XBOX-Y") || target.Contains("TRIANGLE / NORTH"))
            button = Xbox360Button.Y;
        else if (target.Contains("XBOX LB") || target.Contains("LEFT BUMPER") || target.Contains("L1"))
            button = Xbox360Button.LeftShoulder;
        else if (target.Contains("XBOX RB") || target.Contains("RIGHT BUMPER") || target.Contains("R1"))
            button = Xbox360Button.RightShoulder;
        else if (target.Contains("XBOX LSB") || target.Contains("LEFT STICK CLICK") || target.Contains("L3"))
            button = Xbox360Button.LeftThumb;
        else if (target.Contains("XBOX RSB") || target.Contains("RIGHT STICK CLICK") || target.Contains("R3"))
            button = Xbox360Button.RightThumb;
        else if (target.Contains("XBOX MENU") || target.Contains("START") || target.Contains("OPTIONS"))
            button = Xbox360Button.Start;
        else if (target.Contains("XBOX VIEW") || target.Contains("BACK") || target.Contains("SHARE"))
            button = Xbox360Button.Back;
        else if (target.Contains("XBOX GUIDE") || target.Contains("GUIDE") || target.Contains("HOME"))
            button = Xbox360Button.Guide;
        else if (target.Contains("D-PAD UP") || target.Contains("DPADUP"))
            button = Xbox360Button.Up;
        else if (target.Contains("D-PAD DOWN") || target.Contains("DPADDOWN"))
            button = Xbox360Button.Down;
        else if (target.Contains("D-PAD LEFT") || target.Contains("DPADLEFT"))
            button = Xbox360Button.Left;
        else if (target.Contains("D-PAD RIGHT") || target.Contains("DPADRIGHT"))
            button = Xbox360Button.Right;

        if (button != null)
        {
            lock (_lock)
            {
                _controller.SetButtonState(button, isPressed);
                _controller.SubmitReport();
            }
        }
    }

    public void DispatchAxis(string targetOutput, double normalizedValue)
    {
        if (_controller == null && !Initialize()) return;
        if (_controller == null) return;

        string target = targetOutput.ToUpperInvariant();

        short thumbValue = (short)Math.Clamp((normalizedValue * 65535.0) - 32768.0, -32768, 32767);
        byte triggerValue = (byte)Math.Clamp(normalizedValue * 255.0, 0, 255);

        lock (_lock)
        {
            if (target.Contains("LEFT STICK X") || target.Contains("LEFTSTICKX"))
            {
                _controller.SetAxisValue(Xbox360Axis.LeftThumbX, thumbValue);
            }
            else if (target.Contains("LEFT STICK Y") || target.Contains("LEFTSTICKY"))
            {
                _controller.SetAxisValue(Xbox360Axis.LeftThumbY, thumbValue);
            }
            else if (target.Contains("RIGHT STICK X") || target.Contains("RIGHTSTICKX"))
            {
                _controller.SetAxisValue(Xbox360Axis.RightThumbX, thumbValue);
            }
            else if (target.Contains("RIGHT STICK Y") || target.Contains("RIGHTSTICKY"))
            {
                _controller.SetAxisValue(Xbox360Axis.RightThumbY, thumbValue);
            }
            else if (target.Contains("LEFT TRIGGER") || target.Contains("LT"))
            {
                _controller.SetSliderValue(Xbox360Slider.LeftTrigger, triggerValue);
            }
            else if (target.Contains("RIGHT TRIGGER") || target.Contains("RT"))
            {
                _controller.SetSliderValue(Xbox360Slider.RightTrigger, triggerValue);
            }

            _controller.SubmitReport();
        }
    }

    public void Shutdown()
    {
        lock (_lock)
        {
            try
            {
                if (_controller != null)
                {
                    _controller.Disconnect();
                    _controller = null;
                }

                if (_client != null)
                {
                    _client.Dispose();
                    _client = null;
                }
            }
            catch { }
        }
    }

    public void Dispose()
    {
        Shutdown();
    }
}