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
            /*
             * Main/Sub 설정 객체를 먼저 streamConfig에 확정한다.
             * 
             * 기존처럼 지역 변수에만 CreateMain/CreateSub를 넣으면
             * ONVIF 응답 생성 쪽에서는 해당 값이 반영되지 않을 수 있다.
             */
            EnsureStreamQualityConfigs(streamConfig);

            /*
             * 실제 캡처 대상 모니터 해상도를 MainStream 해상도에 반영한다.
             * 
             * MainStream.Width/Height가 0이면 "원본 해상도 유지" 의미이므로,
             * 실제 MonitorInfo.BoundsWidth / BoundsHeight 값을 넣어준다.
             * 
             * 이렇게 해야 FFmpeg 실제 송출 해상도와 ONVIF 설정 응답 해상도가 일치한다.
             */
            ApplyActualMonitorResolution(
                streamConfig,
                monitorInfo);

            StreamQualityConfig mainStream = streamConfig.MainStream;
            StreamQualityConfig subStream = streamConfig.SubStream;

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
             * Main 10fps, Sub 5fps → 화면 캡처 10fps
             * Sub는 filter_complex에서 fps 필터로 5fps까지 낮춘다.
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
             * Main/Sub 둘 다 사용하는 경우.
             * 
             * 입력 화면을 split 한 뒤,
             * Main/Sub 모두 fps 필터를 적용한다.
             * 이렇게 해야 실제 스트리밍 FPS가 설정 FPS를 초과하지 않는다.
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

                string filterComplex = BuildMainSubFilterComplex(
                    mainStream,
                    subStream,
                    monitorInfo);

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

                string videoFilter = BuildVideoFilterOption(
                    mainStream,
                    monitorInfo);

                string arguments =
                    inputOptions +
                    videoFilter +
                    BuildCodecOptions(streamConfig, mainStream) + " " +
                    "-an " +
                    "-f rtsp " +
                    "-rtsp_transport tcp " +
                    mainUrl;

                return arguments;
            }

            /*
             * Sub만 사용하는 경우.
             * 
             * 위에서 useMain && useSub, useMain 조건은 이미 처리되었으므로
             * 여기까지 내려왔다면 useSub만 true인 상태다.
             */
            if (useSub)
            {
                string subUrl = BuildLocalRtspPublishUrl(
                    subStream.RtspPath,
                    rtspServerConfig);

                string videoFilter = BuildVideoFilterOption(
                    subStream,
                    monitorInfo);

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

            /*
             * 이론상 여기까지 올 수 없지만,
             * 컴파일러와 예외 흐름 명확성을 위해 마지막 방어 코드를 둔다.
             */
            throw new InvalidOperationException("FFmpeg Arguments를 생성할 수 없습니다.");
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
        /// NVR 호환성을 위해 H.264 시간정보를 직접 조작하지 않고,
        /// 일반적인 CFR 출력과 x264 파라미터로 고정 프레임레이트 스트림을 생성한다.
        /// 
        /// 정상 동작 채널과 유사하게 GOP는 FPS의 3배로 설정한다.
        /// 예:
        /// - 10fps → GOP 30
        /// - 3fps  → GOP 9
        /// </summary>
        /// <param name="streamConfig">상위 스트림 설정.</param>
        /// <param name="quality">Main/Sub 품질 설정.</param>
        /// <returns>FFmpeg 코덱 옵션 문자열.</returns>
        private string BuildCodecOptions(
            StreamConfig streamConfig,
            StreamQualityConfig quality)
        {
            string codec = streamConfig.Codec ?? "H264";

            int fps = GetSafeFps(quality);

            /*
             * 정상 채널의 패턴이 10fps/GOP30, 3fps/GOP9이므로
             * 우선 GOP를 FPS의 3배로 맞춘다.
             */
            int gop = Math.Max(1, fps * 3);

            string bitrate = NormalizeBitrate(
                quality == null ? "" : quality.Bitrate,
                "1200k");

            string bufferSize = BuildBufferSize(bitrate);

            if (string.Equals(codec, "H265", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(codec, "H.265", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(codec, "HEVC", StringComparison.OrdinalIgnoreCase))
            {
                return "-r " + fps + " " +
                       "-fps_mode cfr " +
                       "-vcodec libx265 " +
                       "-preset ultrafast " +
                       "-pix_fmt yuv420p " +
                       "-g " + gop + " " +
                       "-keyint_min " + gop + " " +
                       "-sc_threshold 0 " +
                       "-b:v " + bitrate + " " +
                       "-maxrate " + bitrate + " " +
                       "-bufsize " + bufferSize;
            }

            return "-r " + fps + " " +
                   "-fps_mode cfr " +
                   "-vcodec libx264 " +
                   "-profile:v baseline " +
                   "-level 3.1 " +
                   "-preset ultrafast " +
                   "-tune zerolatency " +
                   "-pix_fmt yuv420p " +
                   "-x264-params \"keyint=" + gop +
                        ":min-keyint=" + gop +
                        ":scenecut=0" +
                        ":repeat-headers=1" +
                        ":force-cfr=1\" " +
                   "-g " + gop + " " +
                   "-keyint_min " + gop + " " +
                   "-sc_threshold 0 " +
                   "-bf 0 " +
                   "-b:v " + bitrate + " " +
                   "-maxrate " + bitrate + " " +
                   "-bufsize " + bufferSize;
        }



        /// <summary>
        /// 비트레이트 문자열을 FFmpeg에서 사용할 수 있는 형식으로 보정한다.
        /// 
        /// 예:
        /// - 1200k → 1200k
        /// - 2m → 2m
        /// - 1200 → 1200k
        /// </summary>
        /// <param name="bitrate">사용자 설정 비트레이트.</param>
        /// <param name="fallback">비어 있거나 잘못된 경우 사용할 기본값.</param>
        /// <returns>FFmpeg 비트레이트 문자열.</returns>
        private string NormalizeBitrate(
            string bitrate,
            string fallback)
        {
            if (string.IsNullOrWhiteSpace(bitrate))
                return fallback;

            string value = bitrate.Trim().ToLower();

            if (value.EndsWith("k") || value.EndsWith("m"))
                return value;

            int raw;

            if (int.TryParse(value, out raw))
                return raw + "k";

            return fallback;
        }

        /// <summary>
        /// 비트레이트 기준으로 FFmpeg bufsize 값을 생성한다.
        /// 
        /// 일반적으로 CBR에 가깝게 유지하려면 bitrate의 2배 정도를 사용한다.
        /// 예:
        /// - 1200k → 2400k
        /// - 500k → 1000k
        /// </summary>
        /// <param name="bitrate">FFmpeg 비트레이트 문자열.</param>
        /// <returns>FFmpeg bufsize 문자열.</returns>
        private string BuildBufferSize(string bitrate)
        {
            int kbps = ParseBitrateKbps(bitrate);

            if (kbps <= 0)
                return "2400k";

            return (kbps * 2) + "k";
        }

        /// <summary>
        /// 비트레이트 문자열을 kbps 숫자로 변환한다.
        /// 
        /// 예:
        /// - 1200k → 1200
        /// - 2m → 2000
        /// </summary>
        /// <param name="bitrate">비트레이트 문자열.</param>
        /// <returns>kbps 값.</returns>
        private int ParseBitrateKbps(string bitrate)
        {
            if (string.IsNullOrWhiteSpace(bitrate))
                return 0;

            string value = bitrate.Trim().ToLower();

            try
            {
                if (value.EndsWith("k"))
                {
                    value = value.Substring(0, value.Length - 1);

                    int kbps;

                    if (int.TryParse(value, out kbps))
                        return kbps;
                }

                if (value.EndsWith("m"))
                {
                    value = value.Substring(0, value.Length - 1);

                    int mbps;

                    if (int.TryParse(value, out mbps))
                        return mbps * 1000;
                }

                int raw;

                if (int.TryParse(value, out raw))
                    return raw;
            }
            catch
            {
            }

            return 0;
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

        /// <summary>
        /// Main/Sub 동시 출력용 filter_complex 값을 생성한다.
        /// 
        /// 기존 구조에서는 MainStream이 null 필터로 그대로 통과되어
        /// 실제 FPS가 설정값보다 높게 나올 수 있었다.
        /// 
        /// 수정 후에는 Main/Sub 모두 fps 필터를 통과시켜
        /// 실제 RTSP 스트림 FPS를 설정값에 가깝게 제한한다.
        /// </summary>
        /// <param name="mainStream">MainStream 품질 설정.</param>
        /// <param name="subStream">SubStream 품질 설정.</param>
        /// <param name="monitorInfo">현재 캡처 대상 모니터 정보.</param>
        /// <returns>FFmpeg filter_complex 문자열.</returns>
        private string BuildMainSubFilterComplex(
            StreamQualityConfig mainStream,
            StreamQualityConfig subStream,
            MonitorInfo monitorInfo)
        {
            string mainFilter = BuildVideoFilter(
                mainStream,
                monitorInfo);

            string subFilter = BuildVideoFilter(
                subStream,
                monitorInfo);

            return "[0:v]split=2[mainraw][subraw];" +
                   "[mainraw]" + mainFilter + "[mainout];" +
                   "[subraw]" + subFilter + "[subout]";
        }

        /// <summary>
        /// 단일 출력일 때 사용할 -vf 옵션 문자열을 생성한다.
        /// 
        /// Main만 사용하거나 Sub만 사용하는 경우에도
        /// fps 필터를 적용하여 실제 송출 FPS가 설정값을 초과하지 않도록 한다.
        /// </summary>
        /// <param name="quality">송출 품질 설정.</param>
        /// <param name="monitorInfo">현재 캡처 대상 모니터 정보.</param>
        /// <returns>FFmpeg -vf 옵션 문자열.</returns>
        private string BuildVideoFilterOption(
            StreamQualityConfig quality,
            MonitorInfo monitorInfo)
        {
            string filter = BuildVideoFilter(
                quality,
                monitorInfo);

            return "-vf \"" + filter + "\" ";
        }

        /// <summary>
        /// 영상 필터 문자열을 생성한다.
        /// 
        /// fps 필터로 프레임 수만 제한한다.
        /// settb/setpts는 H.264/RTP 시간정보를 NVR이 다르게 해석할 수 있으므로 사용하지 않는다.
        /// </summary>
        /// <param name="quality">송출 품질 설정.</param>
        /// <param name="monitorInfo">현재 캡처 대상 모니터 정보.</param>
        /// <returns>FFmpeg 비디오 필터 문자열.</returns>
        private string BuildVideoFilter(
            StreamQualityConfig quality,
            MonitorInfo monitorInfo)
        {
            int fps = GetSafeFps(quality);

            string filter = "fps=fps=" + fps + ":round=down";

            if (quality != null &&
                quality.Width > 0 &&
                quality.Height > 0 &&
                (quality.Width != monitorInfo.BoundsWidth ||
                 quality.Height != monitorInfo.BoundsHeight))
            {
                filter += ",scale=" + quality.Width + ":" + quality.Height;
            }

            return filter;
        }

        /// <summary>
        /// 품질 설정에서 안전한 FPS 값을 가져온다.
        /// 값이 없거나 잘못된 경우 5fps를 기본값으로 사용한다.
        /// </summary>
        /// <param name="quality">송출 품질 설정.</param>
        /// <returns>1 이상 FPS 값.</returns>
        private int GetSafeFps(StreamQualityConfig quality)
        {
            if (quality == null)
                return 5;

            if (quality.Fps <= 0)
                return 5;

            return quality.Fps;
        }

        /// <summary>
        /// StreamConfig 내부의 MainStream/SubStream 설정 객체를 보장한다.
        /// 
        /// 기존 코드처럼 지역 변수에만 기본 객체를 생성하면,
        /// FFmpeg Arguments 생성에는 사용되지만 ONVIF 응답 생성 쪽의 AppConfig에는 반영되지 않는다.
        /// 그래서 실제 런타임 설정 객체인 streamConfig.MainStream / streamConfig.SubStream에 직접 대입한다.
        /// </summary>
        /// <param name="streamConfig">스트림 설정.</param>
        private void EnsureStreamQualityConfigs(StreamConfig streamConfig)
        {
            if (streamConfig == null)
                throw new ArgumentNullException("streamConfig");

            if (streamConfig.MainStream == null)
            {
                streamConfig.MainStream =
                    StreamQualityConfig.CreateMain(streamConfig.RtspPath);
            }

            if (streamConfig.SubStream == null)
            {
                streamConfig.SubStream =
                    StreamQualityConfig.CreateSub(streamConfig.RtspPath + "_sub");
            }
        }

        /// <summary>
        /// 실제 캡처 대상 모니터 해상도를 MainStream 설정에 반영한다.
        /// 
        /// MainStream.Width/Height가 0이면 원본 모니터 해상도를 사용한다는 의미로 처리한다.
        /// 이 값을 실제 MonitorInfo.BoundsWidth / BoundsHeight로 보정해야
        /// ONVIF GetProfiles / GetVideoEncoderConfigurationOptions 응답과 실제 RTSP 스트림 해상도가 일치한다.
        /// 
        /// SubStream은 보통 640x360처럼 명시된 해상도로 축소 송출하므로,
        /// 값이 이미 있으면 그대로 둔다.
        /// </summary>
        /// <param name="streamConfig">스트림 설정.</param>
        /// <param name="monitorInfo">실제 캡처 대상 모니터 정보.</param>
        private void ApplyActualMonitorResolution(
            StreamConfig streamConfig,
            MonitorInfo monitorInfo)
        {
            if (streamConfig == null)
                throw new ArgumentNullException("streamConfig");

            if (monitorInfo == null)
                throw new ArgumentNullException("monitorInfo");

            if (streamConfig.MainStream == null)
                streamConfig.MainStream = StreamQualityConfig.CreateMain(streamConfig.RtspPath);

            if (streamConfig.SubStream == null)
                streamConfig.SubStream = StreamQualityConfig.CreateSub(streamConfig.RtspPath + "_sub");

            /*
             * MainStream은 기본적으로 실제 화면 원본 해상도와 같아야 한다.
             * Width/Height가 0이면 FFmpeg에서는 scale을 하지 않고 원본으로 나가게 되므로,
             * ONVIF 응답도 실제 원본 해상도를 알려줘야 한다.
             */
            if (streamConfig.MainStream.Width <= 0)
                streamConfig.MainStream.Width = monitorInfo.BoundsWidth;

            if (streamConfig.MainStream.Height <= 0)
                streamConfig.MainStream.Height = monitorInfo.BoundsHeight;

            /*
             * SubStream은 축소 해상도가 설정되어 있으면 그대로 사용한다.
             * 단, 값이 비어 있는 경우에는 실제 화면 해상도를 기준으로 한다.
             * 기본 CreateSub가 640x360을 넣는 구조라면 보통 이 부분은 실행되지 않는다.
             */
            if (streamConfig.SubStream.Width <= 0)
                streamConfig.SubStream.Width = monitorInfo.BoundsWidth;

            if (streamConfig.SubStream.Height <= 0)
                streamConfig.SubStream.Height = monitorInfo.BoundsHeight;
        }
    }
}