using System;
using System.IO;
using pccam_32.Infrastructure;
using pccam_32.Models;

namespace pccam_32.Services
{
    /// <summary>
    /// FFmpeg 실행 관리 서비스.
    /// 
    /// 역할:
    /// 1. FFmpeg 실행 파일 존재 여부 확인
    /// 2. StreamConfig + MonitorInfo + RtspServerConfig 기준으로 FFmpeg 명령 생성
    /// 3. FFmpeg 프로세스 실행
    /// 4. FFmpeg 로그 수신
    /// 5. FFmpeg 종료 처리
    /// 
    /// 주의:
    /// FFmpeg는 정상 실행 중에도 진행 로그를 stderr로 출력한다.
    /// 따라서 ErrorReceived 로그가 있다고 해서 무조건 오류로 판단하면 안 된다.
    /// </summary>
    public class FfmpegService : IDisposable
    {
        private readonly PathProvider _pathProvider;
        private readonly FfmpegCommandBuilder _commandBuilder;
        private readonly ProcessRunner _processRunner;

        private bool _disposed;

        /// <summary>
        /// FFmpeg 로그 수신 이벤트.
        /// Presenter 또는 LogService에서 구독하여 화면 표시 또는 파일 기록에 사용한다.
        /// </summary>
        public event Action<string> LogReceived;

        /// <summary>
        /// FFmpeg 종료 이벤트.
        /// </summary>
        public event Action<int> Exited;

        public FfmpegService(
            PathProvider pathProvider,
            FfmpegCommandBuilder commandBuilder)
        {
            if (pathProvider == null)
                throw new ArgumentNullException("pathProvider");

            if (commandBuilder == null)
                throw new ArgumentNullException("commandBuilder");

            _pathProvider = pathProvider;
            _commandBuilder = commandBuilder;
            _processRunner = new ProcessRunner();

            _processRunner.OutputReceived += OnProcessOutputReceived;
            _processRunner.ErrorReceived += OnProcessErrorReceived;
            _processRunner.Exited += OnProcessExited;
        }

        /// <summary>
        /// FFmpeg 실행 여부.
        /// </summary>
        public bool IsRunning
        {
            get { return _processRunner.IsRunning; }
        }

        /// <summary>
        /// FFmpeg를 실행한다.
        /// 
        /// 처리 순서:
        /// 1. ffmpeg.exe 존재 확인
        /// 2. 스트림 설정 검증
        /// 3. 모니터 정보 검증
        /// 4. FFmpeg Arguments 생성
        /// 5. ProcessRunner로 실행
        /// </summary>
        public void Start(
            StreamConfig streamConfig,
            MonitorInfo monitorInfo,
            RtspServerConfig rtspServerConfig)
        {
            if (_disposed)
                throw new ObjectDisposedException("FfmpegService");

            if (streamConfig == null)
                throw new ArgumentNullException("streamConfig");

            if (monitorInfo == null)
                throw new ArgumentNullException("monitorInfo");

            if (rtspServerConfig == null)
                throw new ArgumentNullException("rtspServerConfig");

            if (!streamConfig.IsEnabled)
                throw new InvalidOperationException("사용하지 않는 스트림은 송출할 수 없습니다.");

            if (IsRunning)
                return;

            _pathProvider.EnsureDirectories();

            string ffmpegPath = _pathProvider.FfmpegExePath;

            if (!File.Exists(ffmpegPath))
            {
                throw new FileNotFoundException(
                    "FFmpeg 실행 파일을 찾을 수 없습니다. External 폴더에 ffmpeg.exe를 배치하세요.",
                    ffmpegPath);
            }

            string arguments = _commandBuilder.BuildArguments(
                streamConfig,
                monitorInfo,
                rtspServerConfig);

            RaiseLog("FFmpeg 실행 파일: " + ffmpegPath);
            RaiseLog("FFmpeg 대상 모니터: " + monitorInfo.DisplayText);
            RaiseLog("FFmpeg 실행 인자: " + arguments);

            _processRunner.Start(
                ffmpegPath,
                arguments,
                _pathProvider.ExternalDirectory);
        }

        /// <summary>
        /// FFmpeg를 종료한다.
        /// 
        /// FFmpeg는 q 입력으로 정상 종료를 시도한다.
        /// 정상 종료가 되지 않으면 ProcessRunner에서 강제 종료한다.
        /// </summary>
        public void Stop()
        {
            if (_disposed)
                return;

            if (!IsRunning)
                return;

            RaiseLog("FFmpeg 종료 요청");

            _processRunner.StopFfmpeg();
        }

        private void OnProcessOutputReceived(string line)
        {
            RaiseLog("[OUT] " + line);
        }

        private void OnProcessErrorReceived(string line)
        {
            /*
             * FFmpeg는 정상 진행 로그도 stderr로 출력한다.
             * 예:
             * frame=...
             * fps=...
             * bitrate=...
             * 
             * 따라서 [ERR] 로그가 있다고 해서 무조건 오류로 처리하지 않는다.
             */
            RaiseLog("[ERR] " + line);
        }

        private void OnProcessExited(int exitCode)
        {
            RaiseLog("FFmpeg 종료됨. ExitCode=" + exitCode);

            Action<int> handler = Exited;
            if (handler != null)
                handler(exitCode);
        }

        private void RaiseLog(string message)
        {
            Action<string> handler = LogReceived;

            if (handler != null)
                handler(message);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                Stop();
            }
            catch
            {
            }

            try
            {
                _processRunner.OutputReceived -= OnProcessOutputReceived;
                _processRunner.ErrorReceived -= OnProcessErrorReceived;
                _processRunner.Exited -= OnProcessExited;
                _processRunner.Dispose();
            }
            catch
            {
            }

            _disposed = true;
        }
    }
}