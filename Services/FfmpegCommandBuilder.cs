using System;
using pccam_32.Models;

namespace pccam_32.Services
{
    /// <summary>
    /// FFmpeg 실행 인자 생성 서비스.
    /// 
    /// StreamConfig:
    /// - FPS
    /// - Bitrate
    /// - Codec
    /// - RtspPath
    /// 
    /// MonitorInfo:
    /// - BoundsX
    /// - BoundsY
    /// - BoundsWidth
    /// - BoundsHeight
    /// 
    /// RtspServerConfig:
    /// - RtspPort
    /// 
    /// 위 값을 조합하여 FFmpeg 실행 인자를 생성한다.
    /// </summary>
    public class FfmpegCommandBuilder
    {
        /// <summary>
        /// FFmpeg 실행 Arguments를 생성한다.
        /// 
        /// 실제 실행 파일 경로(ffmpeg.exe)는 여기서 만들지 않고,
        /// ProcessStartInfo.FileName에서 별도로 지정한다.
        /// </summary>
        public string BuildArguments(
            StreamConfig streamConfig,
            MonitorInfo monitorInfo,
            RtspServerConfig rtspServerConfig)
        {
            if (streamConfig == null)
                throw new ArgumentNullException("streamConfig");

            if (monitorInfo == null)
                throw new ArgumentNullException("monitorInfo");

            if (rtspServerConfig == null)
                throw new ArgumentNullException("rtspServerConfig");

            ValidateStreamConfig(streamConfig);
            ValidateMonitorInfo(monitorInfo);
            ValidateRtspServerConfig(rtspServerConfig);

            string codecOptions = BuildCodecOptions(streamConfig);
            string rtspUrl = BuildLocalRtspPublishUrl(streamConfig, rtspServerConfig);

            /*
             * gdigrab 주요 옵션:
             * -draw_mouse 1
             *   마우스 커서를 항상 표시한다.
             *   PC CAM은 POS 조작 증빙 목적이므로 커서를 숨기는 설정은 제공하지 않는다.
             *
             * -offset_x / -offset_y
             *   듀얼 모니터 환경에서 캡처 시작 좌표를 지정한다.
             *
             * -video_size
             *   캡처할 모니터의 전체 크기를 지정한다.
             */
            string arguments =
                "-f gdigrab " +
                "-draw_mouse 1 " +
                "-framerate " + streamConfig.Fps + " " +
                "-offset_x " + monitorInfo.BoundsX + " " +
                "-offset_y " + monitorInfo.BoundsY + " " +
                "-video_size " + monitorInfo.BoundsWidth + "x" + monitorInfo.BoundsHeight + " " +
                "-i desktop " +
                codecOptions + " " +
                "-an " +
                "-f rtsp " +
                "-rtsp_transport tcp " +
                rtspUrl;

            return arguments;
        }

        /// <summary>
        /// 코덱별 FFmpeg 옵션을 생성한다.
        /// 
        /// 1단계 기본 코덱은 H.264이다.
        /// H.265는 가능은 하지만 저사양 기본값으로는 부적합하므로
        /// 고급 옵션 또는 후속 검토 용도로만 사용한다.
        /// </summary>
        private string BuildCodecOptions(StreamConfig streamConfig)
        {
            string codec = streamConfig.Codec ?? "H264";

            if (string.Equals(codec, "H265", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(codec, "H.265", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(codec, "HEVC", StringComparison.OrdinalIgnoreCase))
            {
                return
                    "-vcodec libx265 " +
                    "-preset ultrafast " +
                    "-pix_fmt yuv420p " +
                    "-b:v " + streamConfig.Bitrate;
            }

            /*
             * H.264 기본 옵션.
             * 
             * -profile:v baseline
             *   구형 장비 및 NVR 호환성을 우선한다.
             *
             * -tune zerolatency
             *   실시간 송출 지연을 줄인다.
             *
             * -pix_fmt yuv420p
             *   VLC/NVR 호환성을 높인다.
             */
            return
                "-vcodec libx264 " +
                "-profile:v baseline " +
                "-level 3.0 " +
                "-preset ultrafast " +
                "-tune zerolatency " +
                "-pix_fmt yuv420p " +
                "-b:v " + streamConfig.Bitrate;
        }

        /// <summary>
        /// FFmpeg가 MediaMTX로 publish할 로컬 RTSP URL을 생성한다.
        /// 
        /// FFmpeg와 MediaMTX는 같은 PC에서 실행되므로,
        /// publish 주소는 127.0.0.1을 사용한다.
        /// 
        /// 외부 VLC/NVR에서 볼 때는 PC의 실제 IP를 사용한다.
        /// 예:
        /// - 내부 publish: rtsp://127.0.0.1:8554/poscam
        /// - 외부 read:    rtsp://192.168.0.10:8554/poscam
        /// </summary>
        private string BuildLocalRtspPublishUrl(
            StreamConfig streamConfig,
            RtspServerConfig rtspServerConfig)
        {
            string path = NormalizeRtspPath(streamConfig.RtspPath);

            return "rtsp://127.0.0.1:" + rtspServerConfig.RtspPort + "/" + path;
        }

        /// <summary>
        /// RTSP 경로에서 앞쪽 / 를 제거한다.
        /// </summary>
        private string NormalizeRtspPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "poscam";

            path = path.Trim();

            while (path.StartsWith("/"))
            {
                path = path.Substring(1);
            }

            return path;
        }

        private void ValidateStreamConfig(StreamConfig streamConfig)
        {
            if (streamConfig.Fps <= 0)
                throw new InvalidOperationException("FPS 값이 올바르지 않습니다.");

            if (string.IsNullOrWhiteSpace(streamConfig.Bitrate))
                throw new InvalidOperationException("Bitrate 값이 비어 있습니다.");

            if (string.IsNullOrWhiteSpace(streamConfig.RtspPath))
                throw new InvalidOperationException("RTSP 경로가 비어 있습니다.");
        }

        private void ValidateMonitorInfo(MonitorInfo monitorInfo)
        {
            if (monitorInfo.BoundsWidth <= 0 || monitorInfo.BoundsHeight <= 0)
                throw new InvalidOperationException("모니터 해상도 정보가 올바르지 않습니다.");
        }

        private void ValidateRtspServerConfig(RtspServerConfig rtspServerConfig)
        {
            if (rtspServerConfig.RtspPort <= 0 || rtspServerConfig.RtspPort > 65535)
                throw new InvalidOperationException("RTSP 포트 값이 올바르지 않습니다.");
        }
    }
}