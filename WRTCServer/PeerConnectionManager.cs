using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WRTCServer
{
    public class PeerConnectionManager : IPeerConnectionManager
    {
        private bool _speakFree;
        private object _lock = new() { };
        private readonly List<string> _connectedUsers;
        private readonly ILogger<PeerConnectionManager> _logger;
        private ConcurrentDictionary<string, List<RTCIceCandidate>> _candidates;
        private ConcurrentDictionary<string, RTCPeerConnection> _peerConnections;

        private static RTCConfiguration _config = new()
        {
            X_UseRtpFeedbackProfile = true,
            iceServers = new List<RTCIceServer>
            {
                new RTCIceServer
                {
                    urls = "stun:stun1.l.google.com:19302"
                },
                new RTCIceServer
                {
                    username = "webrtc",
                    credential = "webrtc",
                    credentialType = RTCIceCredentialType.password,
                    urls = "turn:turn.anyfirewall.com:443?transport=tcp"
                },
            }
        };

        private MediaStreamTrack _audioTrack => new(SDPMediaTypesEnum.audio, false,
              new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(new AudioFormat(AudioCodecsEnum.OPUS, 111, 48000, 2, "minptime=10;maxptime=50;useinbandfec=1;")) }, MediaStreamStatusEnum.SendRecv);

        public PeerConnectionManager(ILogger<PeerConnectionManager> logger)
        {
            _logger = logger;
            _speakFree = true;
            _connectedUsers ??= new List<string>();
            _candidates ??= new ConcurrentDictionary<string, List<RTCIceCandidate>>();
            _peerConnections ??= new ConcurrentDictionary<string, RTCPeerConnection>();
        }

        public async Task<(RTCSessionDescriptionInit, string)> CreateServerOffer()
        {
            try
            {
                var peerConnection = new RTCPeerConnection(_config);

                peerConnection.addTrack(_audioTrack);

                peerConnection.OnAudioFormatsNegotiated += (audioFormats) =>
                {
                    _logger.LogInformation("{OnAudioFormatsNegotiated}");
                };

                peerConnection.OnTimeout += (mediaType) =>
                {
                    _logger.LogWarning("{OnTimeout}");
                };

                peerConnection.ondatachannel += (rdc) =>
                {
                    rdc.onopen += () =>
                    {
                        _logger.LogInformation("datachannel.onopen");
                    };

                    rdc.onclose += () =>
                    {
                        _logger.LogInformation("datachannel.onclose");
                    };

                    rdc.onmessage += (datachan, type, data) =>
                    {
                        try
                        {
                            var (msgType, msg) = ReadDataChannelMessage(Encoding.UTF8.GetString(data));

                            _logger.LogInformation("datachannel.onmessage: {0}", msg);

                            if (msgType == EMessageType.Hello)
                                _connectedUsers.Add(msg);

                            if (msgType == EMessageType.SpeakRequestFinish)
                            {
                                lock (_lock) _speakFree = true;
                                SendMessageToChannels(EMessageType.SuccessFeedback);
                            }

                            if (msgType == EMessageType.SpeakRequestInit)
                            {
                                if (_speakFree)
                                {
                                    lock (_lock) _speakFree = false;
                                    SendMessageToChannels(EMessageType.SuccessFeedback);
                                    SendMessageToChannels(EMessageType.Speaking, new string[] { msg });
                                }
                                else
                                    SendMessageToChannels(EMessageType.ErrorFeedback);
                            }
                        }
                        catch
                        {
                            _logger.LogError("Invalid message received on data channel: {0}", rdc.label);
                        }
                    };
                };

                peerConnection.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) =>
                {
                    _logger.LogInformation("OnStunMessageReceived");
                };

                peerConnection.GetRtpChannel().OnRTPDataReceived += (arg1, arg2, data) =>
                {
                    _logger.LogInformation("RtpChannel.OnRTPDataReceived");
                };

                peerConnection.GetRtpChannel().OnIceCandidate += (candidate) =>
                {
                    _logger.LogInformation("RtpChannel.OnIceCandidate");
                };

                peerConnection.GetRtpChannel().OnIceCandidateError += (candidate, error) =>
                {
                    _logger.LogError("RtpChannelOnIceCandidateError");
                };

                peerConnection.onicecandidateerror += (candidate, error) =>
                {
                    _logger.LogError("onicecandidateerror");
                };

                peerConnection.oniceconnectionstatechange += (state) =>
                {
                    _logger.LogInformation("oniceconnectionstatechange");
                };

                peerConnection.onicegatheringstatechange += (state) =>
                {
                    _logger.LogInformation("onicegatheringstatechange");
                };

                peerConnection.OnSendReport += (media, sr) =>
                {
                    _logger.LogInformation("OnSendReport");
                };

                peerConnection.OnReceiveReport += (arg1, media, sr) =>
                {
                    _logger.LogInformation("OnReceiveReport");
                };

                peerConnection.OnRtcpBye += (reason) =>
                {
                    _logger.LogInformation("OnRtcpBye");
                };

                peerConnection.onicecandidate += (candidate) =>
                {
                    if (peerConnection.signalingState == RTCSignalingState.have_local_offer || peerConnection.signalingState == RTCSignalingState.have_remote_offer)
                    {
                        var candidatesList = _candidates.Where(x => x.Key == peerConnection.SessionID).SingleOrDefault();
                        if (candidatesList.Value is null)
                            _candidates.TryAdd(peerConnection.SessionID, new List<RTCIceCandidate> { candidate });
                        else
                            candidatesList.Value.Add(candidate);
                    }
                };

                peerConnection.onconnectionstatechange += (state) =>
                {
                    _logger.LogInformation("onconnectionstatechange");
                    if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                    {
                        _logger.LogInformation("Peer connection failed | closed | disconected");
                        _peerConnections.TryRemove(peerConnection.SessionID, out _);
                    }
                    else if (state == RTCPeerConnectionState.connected)
                    {
                        _logger.LogInformation("Peer connection connected");
                        peerConnection.DataChannels[0].send(GetDataChannelMessageBytes(EMessageType.Wellcome));
                    }
                };

                peerConnection.OnRtpPacketReceived += (rep, media, pkt) =>
                {
                    if (media == SDPMediaTypesEnum.audio)
                    {
                        var conns = _peerConnections.Where(p => p.Key != peerConnection.SessionID).Select(s => s.Value);
                        foreach (var pc in conns)
                        {
                            if (media == SDPMediaTypesEnum.audio)
                            {
                                pc.SendRtpRaw(SDPMediaTypesEnum.audio, pkt.Payload, pkt.Header.Timestamp, pkt.Header.MarkerBit, pkt.Header.PayloadType);
                            }
                        }
                    }
                };

                await peerConnection.createDataChannel("channel");

                var offerSdp = peerConnection.createOffer(null);

                await peerConnection.setLocalDescription(offerSdp);

                _peerConnections.TryAdd(peerConnection.SessionID, peerConnection);

                SendMessageToChannels(EMessageType.ConnectedUsers);

                return (offerSdp, peerConnection.SessionID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                throw;
            }
        }

        public RTCPeerConnection Get(string id)
        {
            try
            {
                var pc = _peerConnections.Where(pc => pc.Key == id).SingleOrDefault();
                return pc.Value ?? null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                throw;
            }
        }

        public void SetRemoteDescription(string id, RTCSessionDescriptionInit rtcSessionDescriptionInit)
        {
            try
            {
                if (!_peerConnections.TryGetValue(id, out var pc))
                    throw new KeyNotFoundException($"peer connection not found for id: {id}");

                if (rtcSessionDescriptionInit.type != RTCSdpType.answer)
                    throw new InvalidOperationException("server only accepts anwswers for remote description");

                pc.setRemoteDescription(rtcSessionDescriptionInit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                throw;
            }
        }

        public void AddIceCandidate(string id, RTCIceCandidateInit iceCandidate)
        {
            try
            {
                if (!_peerConnections.TryGetValue(id, out var pc))
                    throw new KeyNotFoundException($"peer connection not found for id: {id}");

                pc.addIceCandidate(iceCandidate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                throw;
            }
        }

        public List<RTCIceCandidate> GetIceResults(string id)
        {
            try
            {
                if (!_peerConnections.TryGetValue(id, out var pc))
                    throw new KeyNotFoundException($"peer connection not found for id: {id}");

                if (pc.iceGatheringState != RTCIceGatheringState.complete)
                    throw new Exception($"ice gathering is not completed yet");

                var candidates = _candidates.Where(x => x.Key == id).SingleOrDefault();
                return candidates.Value ?? null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                throw;
            }
        }

        /// <summary>
        ///     Sends a message via the open datachannel for all peers
        /// </summary>
        private void SendMessageToChannels(EMessageType eMessageType, string[] args = null)
        {
            foreach (var conn in _peerConnections.Values)
            {
                conn.DataChannels[0]?.send(GetDataChannelMessageBytes(eMessageType, args));
            }
        }

        /// <summary>
        ///     Reads the msg received on the datachannel
        /// </summary>
        private (EMessageType, string) ReadDataChannelMessage(string msg)
        {
            var splited = msg.Split('|');
            var type = splited[0] switch
            {
                "hello" => EMessageType.Hello,
                "speak_request_init" => EMessageType.SpeakRequestInit,
                "speak_request_finish" => EMessageType.SpeakRequestFinish,
                _ => throw new NotImplementedException()
            };
            return (type, splited[1]);
        }

        /// <summary>
        ///     Gets the string message from datachannel received message
        /// </summary>
        private byte[] GetDataChannelMessageBytes(EMessageType messageType, string[] args = null)
        {
            return messageType switch
            {
                EMessageType.Wellcome => Encoding.UTF8.GetBytes("welcome|Remotatec PS"),
                EMessageType.ConnectedUsers => Encoding.UTF8.GetBytes($"connected_users|{string.Join(",", _connectedUsers)}"),
                EMessageType.Speaking => Encoding.UTF8.GetBytes($"speaking|{args?[0]}"),
                EMessageType.SuccessFeedback => Encoding.UTF8.GetBytes("ok"),
                EMessageType.ErrorFeedback => Encoding.UTF8.GetBytes("nok"),
                _ => throw new NotImplementedException()
            };
        }
    }

    public enum EMessageType
    {
        Wellcome = 0,
        Hello = 1,
        ConnectedUsers = 2,
        SpeakRequestInit = 3,
        SpeakRequestFinish = 4,
        Speaking = 5,
        SuccessFeedback = 6,
        ErrorFeedback = 7
    }
}
