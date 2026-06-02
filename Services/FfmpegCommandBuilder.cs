using System;
using pccam_32.Models;

namespace pccam_32.Services
{
    /// <summary>
    /// FFmpeg 실행 인자 생성 서비스.
    /// 
    /// 현재 구조:
    /// - 하나의 모니터 화면을 캡처한다.
    /// - MainStream은 녹화용 고화질 RTSP로 송출한다.
    /// - SubStream은 실시간 보기용 저화질 RTSP로 송출한다.
    /// 
    /// 예:
    /// Main → rtsp://127.0.0.1:8554/poscam
    /// Sub  → rtsp://127.0.0.1:8554/poscam_sub
    /// </summary>
    public class FfmpegCommandBuilder
    {
        /// <summary>
        /// FFmpeg 실행 Arguments를 생성한다.
        /// 
        /// 실제 실행 파일 경로(ffmpeg.exe)는 여기서 만들지 않고,
        /// ProcessStartInfo.FileName에서 별도로 지정한다.
        /// 
        /// Main/Sub가 모두 활성화되어 있으면 하나의 FFmpeg 프로세스에서
        /// RTSP 출력 2개를 생성한다.
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

            ValidateMonitorInfo(monitorInfo);
            ValidateRtspServerConfig(rtspServerConfig);

            StreamQualityConfig mainStream =
                streamConfig.MainStream ?? StreamQualityConfig.CreateMain(streamConfig.RtspPath);

            StreamQualityConfig subStream =
                streamConfig.SubStream ?? StreamQualityConfig.CreateSub(streamConfig.RtspPath + "_sub");

            bool useMain =
                streamConfig.IsEnabled &&
                mainStream != null &&
                mainStream.IsEnabled;

            bool useSub =
                streamConfig.IsEnabled &&
                subStream != null &&
                subStream.IsEnabled;

            if (!useMain && !useSub)
                throw new InvalidOperationException("사용 가능한 Main/Sub 스트림 설정이 없습니다.");

            if (useMain)
                ValidateStreamQuality(mainStream, "MainStream");

            if (useSub)
                ValidateStreamQuality(subStream, "SubStream");

            /*
             * 캡처 FPS는 활성화된 출력 중 가장 높은 FPS를 사용한다.
             * 예:
             * Main 30fps, Sub 5fps → 화면 캡처 30fps
             * Sub는 filter_complex에서 fps 필터로 낮춘다.
             */
            int captureFps = GetCaptureFps(mainStream, subStream, useMain, useSub);

            string inputOptions =
                "-f gdigrab " +
                "-draw_mouse 1 " +
                "-framerate " + captureFps + " " +
                "-offset_x " + monitorInfo.BoundsX + " " +
                "-offset_y " + monitorInfo.BoundsY + " " +
                "-video_size " + monitorInfo.BoundsWidth + "x" + monitorInfo.BoundsHeight + " " +
                "-i desktop ";

            /*
             * Main/Sub 둘 다 사용하는 경우:
             * 입력 화면을 split 한 뒤
             * Main은 원본 기준으로 송출하고,
             * Sub는 fps/scale 필터를 적용해서 저화질 송출한다.
             */
            if (useMain && useSub)
            {
                /*
                 * Main/Sub는 서로 다른 RTSP 경로를 사용해야 한다.
                 * 같은 경로로 두 번 publish하면 MediaMTX가 두 번째 publish를 거부할 수 있다.
                 */
                ValidateDifferentRtspPaths(mainStream, subStream);

                string mainUrl = BuildLocalRtspPublishUrl(
                    mainStream.RtspPath,
                    rtspServerConfig);

                string subUrl = BuildLocalRtspPublishUrl(
                    subStream.RtspPath,
                    rtspServerConfig);

                string filterComplex =
                    BuildMainSubFilterComplex(subStream);

                string arguments =
                    inputOptions +
                    "-filter_complex \"" + filterComplex + "\" " +

                    "-map \"[mainout]\" " +
                    BuildCodecOptions(streamConfig, mainStream) + " " +
                    "-an " +
                    "-f rtsp " +
                    "-rtsp_transport tcp " +
                    mainUrl + " " +

                    "-map \"[subout]\" " +
                    BuildCodecOptions(streamConfig, subStream) + " " +
                    "-an " +
                    "-f rtsp " +
                    "-rtsp_transport tcp " +
                    subUrl;

                return arguments;
            }

            /*
             * Main만 사용하는 경우.
             */
            if (useMain)
            {
                string mainUrl = BuildLocalRtspPublishUrl(
                    mainStream.RtspPath,
                    rtspServerConfig);

                string arguments =
                    inputOptions +
                    BuildCodecOptions(streamConfig, mainStream) + " " +
                    "-an " +
                    "-f rtsp " +
                    "-rtsp_transport tcp " +
                    mainUrl;

                return arguments;
            }

            /*
             * Sub만 사용하는 경우.
             * 일반적인 기본값은 아니지만 설정상 가능하도록 처리한다.
             */
            {
                string subUrl = BuildLocalRtspPublishUrl(
                    subStream.RtspPath,
                    rtspServerConfig);

                string videoFilter = BuildSubOnlyVideoFilter(subStream);

                string arguments =
                    inputOptions +
                    videoFilter +
                    BuildCodecOptions(streamConfig, subStream) + " " +
                    "-an " +
                    "-f rtsp " +
                    "-rtsp_transport tcp " +
                    subUrl;

                return arguments;
            }
        }


        /// <summary>
        /// MainStream과 SubStream의 RTSP 경로가 서로 다른지 확인한다.
        /// 
        /// 같은 RTSP 경로로 두 개의 출력을 publish하면
        /// MediaMTX에서 두 번째 publish 요청을 거부할 수 있다.
        /// </summary>
        private void ValidateDifferentRtspPaths(
            StreamQualityConfig mainStream,
            StreamQualityConfig subStream)
        {
            string mainPath = NormalizeRtspPath(mainStream == null ? "" : mainStream.RtspPath);
            string subPath = NormalizeRtspPath(subStream == null ? "" : subStream.RtspPath);

            if (string.Equals(mainPath, subPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "MainStream과 SubStream의 RTSP 경로가 같습니다. " +
                    "Main/Sub는 서로 다른 RtspPath를 사용해야 합니다. " +
                    "예: Main=poscam, Sub=poscam_sub");
            }
        }
        /// <summary>
        /// Main/Sub 동시 출력용 filter_complex 값을 생성한다.
        /// 
        /// 입력:
        /// [0:v]
        /// 
        /// 출력:
        /// [mainout] → MainStream
        /// [subout]  → SubStream
        /// 
        /// 주의:
        /// FFmpeg filter_complex 안에서는 copy 필터를 사용할 수 없다.
        /// 원본을 그대로 통과시키려면 null 필터를 사용한다.
        /// </summary>
        /// <param name="subStream">
        /// SubStream 품질 설정.
        /// </param>
        /// <returns>
        /// FFmpeg filter_complex 문자열.
        /// </returns>
        private string BuildMainSubFilterComplex(StreamQualityConfig subStream)
        {
            string subFilter = "fps=" + subStream.Fps;

            if (subStream.Width > 0 && subStream.Height > 0)
            {
                subFilter += ",scale=" + subStream.Width + ":" + subStream.Height;
            }

            return
                "[0:v]split=2[mainraw][subraw];" +
                "[mainraw]null[mainout];" +
                "[subraw]" + subFilter + "[subout]";
        }

        /// <summary>
        /// SubStream만 출력할 때 사용할 비디오 필터 옵션을 생성한다.
        /// </summary>
        /// <param name="subStream">
        /// SubStream 품질 설정.
        /// </param>
        /// <returns>
        /// FFmpeg -vf 옵션 문자열.
        /// </returns>
        private string BuildSubOnlyVideoFilter(StreamQualityConfig subStream)
        {
            string filter = "fps=" + subStream.Fps;

            if (subStream.Width > 0 && subStream.Height > 0)
            {
                filter += ",scale=" + subStream.Width + ":" + subStream.Height;
            }

            return "-vf \"" + filter + "\" ";
        }

        /// <summary>
        /// 활성화된 출력 중 가장 높은 FPS를 캡처 FPS로 사용한다.
        /// </summary>
        private int GetCaptureFps(
            StreamQualityConfig mainStream,
            StreamQualityConfig subStream,
            bool useMain,
            bool useSub)
        {
            int fps = 1;

            if (useMain && mainStream != null && mainStream.Fps > fps)
                fps = mainStream.Fps;

            if (useSub && subStream != null && subStream.Fps > fps)
                fps = subStream.Fps;

            return fps;
        }

        /// <summary>
        /// 코덱별 FFmpeg 옵션을 생성한다.
        /// 
        /// Codec은 StreamConfig의 공통 Codec 값을 사용하고,
        /// Bitrate는 Main/Sub 각각의 품질 설정값을 사용한다.
        /// </summary>
        private string BuildCodecOptions(
            StreamConfig streamConfig,
            StreamQualityConfig quality)
        {
            string codec = streamConfig.Codec ?? "H264";
            int gop = Math.Max(1, quality.Fps);


            if (string.Equals(codec, "H265", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(codec, "H.265", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(codec, "HEVC", StringComparison.OrdinalIgnoreCase))    
            {
                return
                    "-vcodec libx265 " +
                    "-preset ultrafast " +
                    "-pix_fmt yuv420p " +
                    "-g " + gop + " " +
                    "-keyint_min " + gop + " " +
                    "-sc_threshold 0 " +
                    "-b:v " + quality.Bitrate;
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
                "-g " + gop + " " +
                "-keyint_min " + gop + " " +
                "-sc_threshold 0 " +
                "-b:v " + quality.Bitrate;
        }

        /// <summary>
        /// FFmpeg가 MediaMTX로 publish할 로컬 RTSP URL을 생성한다.
        /// 
        /// FFmpeg와 MediaMTX는 같은 PC에서 실행되므로,
        /// publish 주소는 127.0.0.1을 사용한다.
        /// </summary>
        private string BuildLocalRtspPublishUrl(
            string rtspPath,
            RtspServerConfig rtspServerConfig)
        {
            string path = NormalizeRtspPath(rtspPath);

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

        /// <summary>
        /// Main/Sub 품질 설정값을 검증한다.
        /// </summary>
        private void ValidateStreamQuality(
            StreamQualityConfig quality,
            string name)
        {
            if (quality == null)
                throw new InvalidOperationException(name + " 설정이 없습니다.");

            if (quality.Fps <= 0)
                throw new InvalidOperationException(name + " FPS 값이 올바르지 않습니다.");

            if (string.IsNullOrWhiteSpace(quality.Bitrate))
                throw new InvalidOperationException(name + " Bitrate 값이 비어 있습니다.");

            if (string.IsNullOrWhiteSpace(quality.RtspPath))
                throw new InvalidOperationException(name + " RTSP 경로가 비어 있습니다.");
        }

        /// <summary>
        /// 모니터 해상도 정보를 검증한다.
        /// </summary>
        private void ValidateMonitorInfo(MonitorInfo monitorInfo)
        {
            if (monitorInfo.BoundsWidth <= 0 || monitorInfo.BoundsHeight <= 0)
                throw new InvalidOperationException("모니터 해상도 정보가 올바르지 않습니다.");
        }

        /// <summary>
        /// RTSP 서버 설정값을 검증한다.
        /// </summary>
        private void ValidateRtspServerConfig(RtspServerConfig rtspServerConfig)
        {
            if (rtspServerConfig.RtspPort <= 0 || rtspServerConfig.RtspPort > 65535)
                throw new InvalidOperationException("RTSP 포트 값이 올바르지 않습니다.");
        }
    }
}