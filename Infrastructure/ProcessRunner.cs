using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace pccam_32.Infrastructure
{
    /// <summary>
    /// 외부 실행 파일 프로세스 실행/종료 관리 클래스.
    /// 
    /// PC CAM에서는 아래 외부 프로그램을 실행하기 위해 사용한다.
    /// - ffmpeg.exe
    /// - mediamtx_final_32bit.exe
    /// 
    /// 이 클래스는 하나의 외부 프로세스만 관리한다.
    /// 따라서 FFmpeg용, MediaMTX용으로 각각 인스턴스를 따로 생성하는 방식이 안전하다.
    /// </summary>
    public class ProcessRunner : IDisposable
    {
        private readonly object _syncLock = new object();

        private Process _process;
        private bool _disposed;

        /// <summary>
        /// 표준 출력 로그 수신 이벤트.
        /// MediaMTX는 주로 표준 출력으로 로그가 나올 수 있다.
        /// </summary>
        public event Action<string> OutputReceived;

        /// <summary>
        /// 표준 오류 로그 수신 이벤트.
        /// FFmpeg는 정상 동작 중에도 진행 로그를 stderr로 출력하는 경우가 많다.
        /// 따라서 ErrorReceived라고 해서 무조건 오류로 판단하면 안 된다.
        /// </summary>
        public event Action<string> ErrorReceived;

        /// <summary>
        /// 프로세스 종료 이벤트.
        /// </summary>
        public event Action<int> Exited;

        /// <summary>
        /// 현재 프로세스가 실행 중인지 여부.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (_syncLock)
                {
                    return _process != null && !_process.HasExited;
                }
            }
        }

        /// <summary>
        /// 현재 실행 중인 프로세스 ID.
        /// 실행 중이 아니면 null을 반환한다.
        /// </summary>
        public int? ProcessId
        {
            get
            {
                lock (_syncLock)
                {
                    if (_process == null || _process.HasExited)
                        return null;

                    return _process.Id;
                }
            }
        }

        /// <summary>
        /// 외부 프로세스를 시작한다.
        /// 
        /// exePath:
        /// - 실행 파일 전체 경로
        /// 
        /// arguments:
        /// - 실행 인자
        /// 
        /// workingDirectory:
        /// - 실행 기준 폴더
        /// - null이면 exePath의 폴더를 사용한다.
        /// </summary>
        public void Start(string exePath, string arguments, string workingDirectory = null)
        {
            if (_disposed)
                throw new ObjectDisposedException("ProcessRunner");

            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentException("실행 파일 경로가 비어 있습니다.", "exePath");

            if (!File.Exists(exePath))
                throw new FileNotFoundException("실행 파일을 찾을 수 없습니다.", exePath);

            lock (_syncLock)
            {
                if (_process != null && !_process.HasExited)
                    throw new InvalidOperationException("이미 실행 중인 프로세스가 있습니다.");

                string resolvedWorkingDirectory = workingDirectory;

                if (string.IsNullOrWhiteSpace(resolvedWorkingDirectory))
                    resolvedWorkingDirectory = Path.GetDirectoryName(exePath);

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = exePath;
                startInfo.Arguments = arguments ?? "";
                startInfo.WorkingDirectory = resolvedWorkingDirectory;

                /*
                 * UseShellExecute = false
                 * - 표준 출력/오류 리다이렉션을 사용하기 위해 필요하다.
                 * 
                 * CreateNoWindow = true
                 * - FFmpeg, MediaMTX 콘솔창이 사용자에게 보이지 않도록 한다.
                 */
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;

                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.RedirectStandardInput = true;

                /*
                 * Windows 7 한글 환경에서 로그가 깨지는 것을 줄이기 위한 설정.
                 * FFmpeg/MediaMTX 로그는 대부분 영문이므로 큰 문제는 없지만,
                 * 우선 시스템 기본 인코딩을 사용한다.
                 */
                startInfo.StandardOutputEncoding = Encoding.Default;
                startInfo.StandardErrorEncoding = Encoding.Default;

                _process = new Process();
                _process.StartInfo = startInfo;
                _process.EnableRaisingEvents = true;

                _process.OutputDataReceived += Process_OutputDataReceived;
                _process.ErrorDataReceived += Process_ErrorDataReceived;
                _process.Exited += Process_Exited;

                bool started = _process.Start();

                if (!started)
                    throw new InvalidOperationException("프로세스를 시작하지 못했습니다.");

                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
        }

        /// <summary>
        /// 실행 중인 프로세스에 입력값을 전달한다.
        /// 
        /// FFmpeg는 q 입력을 받으면 정상 종료를 시도한다.
        /// </summary>
        public void SendInputLine(string text)
        {
            lock (_syncLock)
            {
                if (_process == null || _process.HasExited)
                    return;

                try
                {
                    _process.StandardInput.WriteLine(text ?? "");
                    _process.StandardInput.Flush();
                }
                catch
                {
                    // 입력 전달 실패는 종료 처리 중 발생할 수 있으므로 무시한다.
                }
            }
        }

        /// <summary>
        /// 프로세스를 종료한다.
        /// 
        /// gracefulInput:
        /// - FFmpeg 종료 시 "q"를 전달하면 정상 종료 가능성이 높다.
        /// 
        /// waitMilliseconds:
        /// - 정상 종료 대기 시간.
        /// - 시간이 지나도 종료되지 않으면 Kill을 수행한다.
        /// </summary>
        public void Stop(string gracefulInput = null, int waitMilliseconds = 5000)
        {
            lock (_syncLock)
            {
                if (_process == null)
                    return;

                if (_process.HasExited)
                {
                    CleanupProcess();
                    return;
                }

                try
                {
                    if (!string.IsNullOrEmpty(gracefulInput))
                    {
                        try
                        {
                            _process.StandardInput.WriteLine(gracefulInput);
                            _process.StandardInput.Flush();
                        }
                        catch
                        {
                            // 입력 전달 실패 시 강제 종료로 넘어간다.
                        }
                    }

                    bool exited = _process.WaitForExit(waitMilliseconds);

                    if (!exited)
                    {
                        try
                        {
                            _process.Kill();
                            _process.WaitForExit(3000);
                        }
                        catch
                        {
                            // 이미 종료되었거나 권한 문제 등이 있을 수 있으므로 무시한다.
                        }
                    }
                }
                finally
                {
                    CleanupProcess();
                }
            }
        }

        /// <summary>
        /// FFmpeg 종료용 편의 메서드.
        /// FFmpeg는 q 입력으로 종료를 시도한다.
        /// </summary>
        public void StopFfmpeg()
        {
            Stop("q", 5000);
        }

        /// <summary>
        /// MediaMTX 종료용 편의 메서드.
        /// 1단계에서는 별도 콘솔 제어 신호 없이 종료한다.
        /// </summary>
        public void StopMediaMtx()
        {
            Stop(null, 3000);
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            Action<string> handler = OutputReceived;
            if (handler != null)
                handler(e.Data);
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            Action<string> handler = ErrorReceived;
            if (handler != null)
                handler(e.Data);
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            int exitCode = -1;

            try
            {
                if (_process != null)
                    exitCode = _process.ExitCode;
            }
            catch
            {
                exitCode = -1;
            }

            Action<int> handler = Exited;
            if (handler != null)
                handler(exitCode);
        }

        private void CleanupProcess()
        {
            if (_process == null)
                return;

            try
            {
                _process.OutputDataReceived -= Process_OutputDataReceived;
                _process.ErrorDataReceived -= Process_ErrorDataReceived;
                _process.Exited -= Process_Exited;
            }
            catch
            {
            }

            try
            {
                _process.Dispose();
            }
            catch
            {
            }

            _process = null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                Stop(null, 1000);
            }
            catch
            {
            }

            _disposed = true;
        }
    }
}