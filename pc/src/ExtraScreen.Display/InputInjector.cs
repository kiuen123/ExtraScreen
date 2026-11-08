using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ExtraScreen.Protocol;

namespace ExtraScreen.Display;

/// <summary>
/// Bộ tiêm cảm ứng đa điểm (Multi-Touch 10 ngón) & chuột trên Windows.
/// Kết hợp:
/// 1. Windows Touch Injection API (InitializeTouchInjection / InjectTouchInput) cho cảm ứng đa điểm thực sự.
/// 2. Máy trạng thái cảm ứng sang chuột (AL_FSTM_TOUCH_EVENT_DOWN_AFTER_GT500MS -> Right Click) trích xuất từ spacedeskService.exe (qua REA).
/// </summary>
[SupportedOSPlatform("windows")]
public static class InputInjector
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    private const int INPUT_MOUSE = 0;

    // Hằng số Windows Touch Injection
    private const uint MAX_TOUCH_COUNT = 10;
    private const uint TOUCH_FEEDBACK_DEFAULT = 0x1;
    private const uint PT_TOUCH = 0x00000002;

    private const uint POINTER_FLAG_NONE = 0x00000000;
    private const uint POINTER_FLAG_NEW = 0x00000001;
    private const uint POINTER_FLAG_INRANGE = 0x00000002;
    private const uint POINTER_FLAG_INCONTACT = 0x00000004;
    private const uint POINTER_FLAG_PRIMARY = 0x00002000;
    private const uint POINTER_FLAG_DOWN = 0x00010000;
    private const uint POINTER_FLAG_UPDATE = 0x00020000;
    private const uint POINTER_FLAG_UP = 0x00040000;
    private const uint POINTER_FLAG_CANCELED = 0x00080000;

    private const uint TOUCH_MASK_CONTACTAREA = 0x00000001;
    private const uint TOUCH_MASK_ORIENTATION = 0x00000002;
    private const uint TOUCH_MASK_PRESSURE = 0x00000004;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_INFO
    {
        public uint pointerType;
        public uint pointerId;
        public uint frameId;
        public uint pointerFlags;
        public IntPtr sourceDevice;
        public IntPtr hwndTarget;
        public POINT ptPixelLocation;
        public POINT ptHimetricLocation;
        public POINT ptPixelLocationRaw;
        public POINT ptHimetricLocationRaw;
        public uint dwTime;
        public uint historyCount;
        public int inputData;
        public uint dwKeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint touchFlags;
        public uint touchMask;
        public RECT rcContact;
        public RECT rcContactRaw;
        public uint orientation;
        public uint pressure;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)]
        public int type;
        [FieldOffset(8)]
        public MOUSEINPUT mi;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InitializeTouchInjection(uint maxCount, uint dwMode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InjectTouchInput(uint count, [In] POINTER_TOUCH_INFO[] contacts);

    private static bool _touchInjectionInitialized;
    private static readonly object _initLock = new();
    private static readonly ConcurrentDictionary<int, TouchPointState> _activeContacts = new();

    private sealed class TouchPointState
    {
        public int PointerId;
        public int StartX;
        public int StartY;
        public int CurrentX;
        public int CurrentY;
        public uint Pressure;
        public long DownTimestampMs;
        public bool MovedSignificantly;
    }

    public static bool UseNativeMultiTouch { get; set; } = true;

    private static bool EnsureTouchInjectionInitialized()
    {
        if (_touchInjectionInitialized) return true;
        lock (_initLock)
        {
            if (!_touchInjectionInitialized)
            {
                _touchInjectionInitialized = InitializeTouchInjection(MAX_TOUCH_COUNT, TOUCH_FEEDBACK_DEFAULT);
            }
            return _touchInjectionInitialized;
        }
    }

    /// <summary>
    /// Chuyển đổi tọa độ touch chuẩn hóa [0..1] từ Android thành sự kiện Multi-Touch (hoặc Chuột có cử chỉ) trên Windows
    /// ánh xạ chính xác tới màn hình phụ được chọn.
    /// </summary>
    public static void InjectTouch(TouchEvent touch, DisplayOutputInfo display)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        int targetX = display.PositionX + (int)Math.Round(touch.NormX * display.Width);
        int targetY = display.PositionY + (int)Math.Round(touch.NormY * display.Height);

        if (UseNativeMultiTouch && EnsureTouchInjectionInitialized())
        {
            if (TryInjectNativeTouch(touch, targetX, targetY))
            {
                return;
            }
        }

        InjectMouseWithGestureStateMachine(touch, targetX, targetY);
    }

    private static bool TryInjectNativeTouch(TouchEvent touch, int pixelX, int pixelY)
    {
        uint pressureVal = (uint)Math.Clamp((int)(touch.Pressure * 1024f), 1, 1024);
        long nowMs = Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;

        if (touch.Action == ProtocolConstants.TouchActionDown)
        {
            _activeContacts[touch.PointerId] = new TouchPointState
            {
                PointerId = touch.PointerId,
                StartX = pixelX,
                StartY = pixelY,
                CurrentX = pixelX,
                CurrentY = pixelY,
                Pressure = pressureVal,
                DownTimestampMs = nowMs
            };
        }
        else if (_activeContacts.TryGetValue(touch.PointerId, out var existing))
        {
            existing.CurrentX = pixelX;
            existing.CurrentY = pixelY;
            existing.Pressure = pressureVal;
        }

        uint flags = touch.Action switch
        {
            ProtocolConstants.TouchActionDown => POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT,
            ProtocolConstants.TouchActionMove => POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT,
            ProtocolConstants.TouchActionUp => POINTER_FLAG_UP,
            ProtocolConstants.TouchActionCancel => POINTER_FLAG_UP | POINTER_FLAG_CANCELED,
            _ => POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT
        };

        if (touch.PointerId == 0)
        {
            flags |= POINTER_FLAG_PRIMARY;
        }

        var contact = new POINTER_TOUCH_INFO
        {
            pointerInfo = new POINTER_INFO
            {
                pointerType = PT_TOUCH,
                pointerId = (uint)Math.Clamp(touch.PointerId, 0, (int)MAX_TOUCH_COUNT - 1),
                pointerFlags = flags,
                ptPixelLocation = new POINT { X = pixelX, Y = pixelY }
            },
            touchFlags = 0,
            touchMask = TOUCH_MASK_CONTACTAREA | TOUCH_MASK_ORIENTATION | TOUCH_MASK_PRESSURE,
            orientation = 90,
            pressure = pressureVal,
            rcContact = new RECT
            {
                Left = pixelX - 2,
                Top = pixelY - 2,
                Right = pixelX + 2,
                Bottom = pixelY + 2
            }
        };

        bool ok = InjectTouchInput(1, new[] { contact });

        if (touch.Action == ProtocolConstants.TouchActionUp || touch.Action == ProtocolConstants.TouchActionCancel)
        {
            _activeContacts.TryRemove(touch.PointerId, out _);
        }

        return ok;
    }

    /// <summary>
    /// Máy trạng thái AL_FSTM_TOUCH_EVENT_* học từ spacedeskService.exe:
    /// - Chạm & di chuyển: kéo chuột trái
    /// - Chạm giữ yên > 500ms rồi nhả (AL_FSTM_TOUCH_EVENT_DOWN_AFTER_GT500MS / UP_RIGHT_CLICK): Click chuột phải
    /// </summary>
    private static void InjectMouseWithGestureStateMachine(TouchEvent touch, int targetX, int targetY)
    {
        int vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (vWidth <= 0 || vHeight <= 0) return;

        int normalizedX = (int)Math.Round((targetX - vLeft) * 65535.0 / Math.Max(1, vWidth - 1));
        int normalizedY = (int)Math.Round((targetY - vTop) * 65535.0 / Math.Max(1, vHeight - 1));

        long nowMs = Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;
        uint flags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;

        switch (touch.Action)
        {
            case ProtocolConstants.TouchActionDown:
                _activeContacts[touch.PointerId] = new TouchPointState
                {
                    PointerId = touch.PointerId,
                    StartX = targetX,
                    StartY = targetY,
                    CurrentX = targetX,
                    CurrentY = targetY,
                    DownTimestampMs = nowMs,
                    MovedSignificantly = false
                };
                flags |= MOUSEEVENTF_LEFTDOWN;
                break;

            case ProtocolConstants.TouchActionMove:
                if (_activeContacts.TryGetValue(touch.PointerId, out var state))
                {
                    if (Math.Abs(targetX - state.StartX) > 10 || Math.Abs(targetY - state.StartY) > 10)
                    {
                        state.MovedSignificantly = true;
                    }
                }
                break;

            case ProtocolConstants.TouchActionUp:
            case ProtocolConstants.TouchActionCancel:
                flags |= MOUSEEVENTF_LEFTUP;
                if (_activeContacts.TryRemove(touch.PointerId, out var upState))
                {
                    long heldMs = nowMs - upState.DownTimestampMs;
                    // AL_FSTM_TOUCH_EVENT_DOWN_AFTER_GT500MS -> UP_RIGHT_CLICK
                    if (!upState.MovedSignificantly && heldMs >= 500 && touch.Action == ProtocolConstants.TouchActionUp)
                    {
                        SendMouseInput(normalizedX, normalizedY, flags);
                        SendMouseInput(normalizedX, normalizedY, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP);
                        return;
                    }
                }
                break;
        }

        SendMouseInput(normalizedX, normalizedY, flags);
    }

    private static void SendMouseInput(int normalizedX, int normalizedY, uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = normalizedX,
                dy = normalizedY,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }
}
