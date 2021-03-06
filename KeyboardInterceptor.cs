﻿//
// Authors:
//   Lucas Ontivero lucasontivero@gmail.com
//
// Copyright (C) 2014 Lucas Ontivero
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Open.WinKeyboardHook
{
    public sealed class KeyboardInterceptor : IKeyboardInterceptor
    {
        private static DeadKeyInfo _lastDeadKey;
        private readonly KeysConverter _keyConverter = new KeysConverter();

        public event EventHandler<KeyEventArgs> KeyDown;
        public event EventHandler<KeyPressEventArgs> KeyPress;
        public event EventHandler<KeyEventArgs> KeyUp;
        private LowLevelKeyboardProc _keyboardProc;
        private SafeWinHandle _previousKeyboardHandler;

        public void StartCapturing()
        {
            if (_previousKeyboardHandler == null || _previousKeyboardHandler.IsClosed)
            {
                HookKeyboard();
            }
        }

        public void StopCapturing()
        {
            _previousKeyboardHandler?.Close();
        }

        private void HookKeyboard()
        {
            _keyboardProc = keyboardHandler;
            using (var process = Process.GetCurrentProcess())
            {
                using (var module = process.MainModule)
                {
                    var moduleHandler = NativeMethods.GetModuleHandle(module.ModuleName);

                    _previousKeyboardHandler = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD, _keyboardProc, moduleHandler, 0);

                    if (_previousKeyboardHandler.IsInvalid)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
            }
        }

        private IntPtr keyboardHandler(int nCode, IntPtr wParam, ref KBDLLHOOKSTRUCT kbdStruct)
        {
            IntPtr ret;

            try
            {
                if (nCode >= 0)
                {
                    var virtualKeyCode = (Keys) kbdStruct.KeyCode;
                    var keyData = BuildKeyData(virtualKeyCode);
                    var keyEventArgs = new KeyEventArgs(keyData);

                    var intParam = wParam.ToInt32();
                    switch (intParam)
                    {
                        case NativeMethods.WM_SYSKEYDOWN:
                        case NativeMethods.WM_KEYDOWN:
                            RaiseKeyDownEvent(keyEventArgs);

                            var keyState = new byte[256];
                            if (!NativeMethods.GetKeyboardState(keyState)) break;

                            var buffer = ToUnicode(kbdStruct, keyState);
                            if (!string.IsNullOrEmpty(buffer))
                            {
                                foreach (var rawKey in buffer)
                                {
                                    var s = _keyConverter.ConvertToString(rawKey);
                                    if (s == null) continue;

                                    var key = s[0];
                                    RaiseKeyPressEvent(key);
                                }
                            }
                            break;
                        case NativeMethods.WM_SYSKEYUP:
                        case NativeMethods.WM_KEYUP:
                            RaiseKeyUpEvent(keyEventArgs);
                            break;
                    }
                    if (keyEventArgs.SuppressKeyPress) return new IntPtr(1);
                }
            }
            finally
            {
                ret = NativeMethods.CallNextHookEx(_previousKeyboardHandler, nCode, wParam, ref kbdStruct);
            }

            return ret;
        }


        private static Keys BuildKeyData(Keys virtualKeyCode)
        {
            var isDownControl = IsControlKeyDown();
            var isDownShift = IsShiftKeyDown();
            var isDownAlt = IsAltKeyDown();
            var isAltGr = IsAltGrKeyDown();

            return virtualKeyCode |
                   (isDownControl ? Keys.Control : Keys.None) |
                   (isDownShift ? Keys.Shift : Keys.None) |
                   (isDownAlt ? Keys.Alt : Keys.None) |
                   (isAltGr ? (Keys) 524288 : Keys.None);
        }

        private static bool IsKeyPressed(byte virtualKeyCode)
        {
            return (NativeMethods.GetKeyState(virtualKeyCode) & 0x80) != 0;
        }

        private static bool IsControlKeyDown()
        {
            return IsKeyPressed(NativeMethods.VK_LCONTROL) || IsKeyPressed(NativeMethods.VK_RCONTROL);
        }

        private static bool IsShiftKeyDown()
        {
            return IsKeyPressed(NativeMethods.VK_LSHIFT) || IsKeyPressed(NativeMethods.VK_RSHIFT);
        }

        private static bool IsAltKeyDown()
        {
            return IsKeyPressed(NativeMethods.VK_LALT) || IsKeyPressed(NativeMethods.VK_RALT);
        }

        private static bool IsAltGrKeyDown()
        {
            return IsKeyPressed(NativeMethods.VK_RMENU) || IsControlKeyDown() && IsAltKeyDown();
        }

        private void RaiseKeyPressEvent(char key)
        {
            KeyPress?.Invoke(this, new KeyPressEventArgs(key));
        }

        private void RaiseKeyDownEvent(KeyEventArgs args)
        {
            KeyDown?.Invoke(this, args);
        }

        private void RaiseKeyUpEvent(KeyEventArgs args)
        {
            KeyUp?.Invoke(this, args);
        }

        private static string ToUnicode(KBDLLHOOKSTRUCT info, byte[] keyState)
        {
            string result = null;

            if (IsAltGrKeyDown()) keyState[NativeMethods.VK_LCONTROL] = keyState[NativeMethods.VK_LALT] = 0x80;

            var buffer = new StringBuilder(128);
            var layout = GetForegroundKeyboardLayout();
            var count = ToUnicode((Keys) info.KeyCode, info.KeyCode, keyState, buffer, layout);

            if (count > 0)
            {
                result = buffer.ToString(0, count);

                var isShiftDown = IsShiftKeyDown();
                var capsLock = IsKeyPressed(NativeMethods.VK_CAPITAL);
                var isShift = IsKeyPressed(NativeMethods.VK_SHIFT);
                var state = (keyState[NativeMethods.VK_LSHIFT] >= 0x80);
                if (result == "B")
                {

                }
                else if (result == "b")
                { }
                else if (result == "8")
                { }

                if (isShiftDown != state)
                { }

                if (_lastDeadKey == null) return result;

                // Reload diacritic character or accent
                ToUnicode(_lastDeadKey.KeyCode, _lastDeadKey.ScanCode, _lastDeadKey.KeyboardState, buffer, layout);
                count = ToUnicode((Keys)info.KeyCode, info.ScanCode, keyState, buffer, layout);

                result = buffer.ToString(0, count);

                _lastDeadKey = null;
            }
            else if (count < 0)
            {
                _lastDeadKey = new DeadKeyInfo(info, keyState);

                while (count < 0)
                {
                    count = ToUnicode(Keys.Decimal, buffer, layout);
                }
            }

            return result;
        }

        private static IntPtr GetForegroundKeyboardLayout()
        {
            var foregroundWnd = NativeMethods.GetForegroundWindow();

            if (foregroundWnd != IntPtr.Zero)
            {
                uint processId;
                var threadId = NativeMethods.GetWindowThreadProcessId(foregroundWnd, out processId);

                return NativeMethods.GetKeyboardLayout(threadId);
            }

            return IntPtr.Zero;
        }

        private static int ToUnicode(Keys vk, StringBuilder buffer, IntPtr hkl)
        {
            return ToUnicode(vk, ToScanCode(vk), new byte[256], buffer, hkl);
        }

        private static int ToUnicode(Keys vk, uint sc, byte[] keyState, StringBuilder buffer, IntPtr hkl)
        {
            return NativeMethods.ToUnicodeEx((uint) vk, sc, keyState, buffer, buffer.Capacity, 0, hkl);
        }

        private static uint ToScanCode(Keys vk)
        {
            return NativeMethods.MapVirtualKey((uint) vk, 0);
        }

        #region Nested type: DeadKeyInfo

        private sealed class DeadKeyInfo
        {
            public readonly Keys KeyCode;
            public readonly byte[] KeyboardState;
            public readonly uint ScanCode;

            public DeadKeyInfo(KBDLLHOOKSTRUCT info, byte[] keyState)
            {
                KeyCode = (Keys) info.KeyCode;
                ScanCode = info.ScanCode;

                KeyboardState = keyState;
            }
        }

        #endregion
    }
}