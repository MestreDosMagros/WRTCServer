using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WRTCServer
{
    public class PeerConnectionManager : IPeerConnectionManager
    {
        private readonly ILogger<PeerConnectionManager> _logger;

        private System.Timers.Timer _timer;

        private Dictionary<string, bool> _audioSet;
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
              new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(new AudioFormat(AudioCodecsEnum.OPUS, 111, 48000, 2, "minptime=10;maxptime=50;useinbandfec=1;")) }, MediaStreamStatusEnum.SendRecv)
        {
            //SdpSsrc = new Dictionary<uint, SDPSsrcAttribute>() { { 99, new SDPSsrcAttribute(99, "default") }
        };

        public PeerConnectionManager(ILogger<PeerConnectionManager> logger)
        {
            _logger = logger;

            _audioSet = new Dictionary<string, bool>();
            _candidates ??= new ConcurrentDictionary<string, List<RTCIceCandidate>>();
            _peerConnections ??= new ConcurrentDictionary<string, RTCPeerConnection>();

            _timer = new System.Timers.Timer(TimeSpan.FromSeconds(15).TotalMilliseconds);
            _timer.Elapsed += SetAudioMuted;
            _timer.Start();
        }

        // BAGACEI
        private void SetAudioMuted(object sender, System.Timers.ElapsedEventArgs e)
        {
            var rnd = new Random();
            try
            {
                if(_audioSet.Any())
                {
                    string unmutedKey = string.Empty;
                    if (_audioSet.Any(s => s.Value == false))
                    {
                        unmutedKey = _audioSet.Where(s => s.Value == false).Single().Key;
                        _audioSet[unmutedKey] = true;
                    }

                    var keys = _audioSet.Keys.Where(k => k != unmutedKey).ToArray();
                    _audioSet[keys[rnd.Next(0, keys.Length)]] = false;
                }

                _audioSet.ToList().ForEach(x =>
                {
                    _logger.LogInformation($"{x.Key} {x.Value}");
                });
            }
            catch (Exception ex)
            {
                throw;
            }
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
                        _logger.LogInformation("{datachannel.onopen}");
                    };

                    rdc.onclose += () =>
                    {
                        _logger.LogInformation("{datachannel.onclose}");
                    };

                    rdc.onmessage += (datachan, type, data) =>
                    {
                        _logger.LogInformation("{datachannel.onmessage}");
                    };
                };

                peerConnection.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) =>
                {
                    _logger.LogInformation("{OnStunMessageReceived}");
                };

                peerConnection.GetRtpChannel().OnRTPDataReceived += (arg1, arg2, data) =>
                {
                    _logger.LogInformation("{GetRtpChannel().OnRTPDataReceived}");
                };

                peerConnection.GetRtpChannel().OnIceCandidate += (candidate) =>
                {
                    _logger.LogInformation("{GetRtpChannel().OnIceCandidate}");
                };

                peerConnection.GetRtpChannel().OnIceCandidateError += (candidate, error) =>
                {
                    _logger.LogError("{GetRtpChannel().OnIceCandidateError}");
                };

                peerConnection.onicecandidateerror += (candidate, error) =>
                {
                    _logger.LogError("{onicecandidateerror}");
                };

                peerConnection.oniceconnectionstatechange += (state) =>
                {
                    _logger.LogInformation("{oniceconnectionstatechange}");
                };

                peerConnection.onicegatheringstatechange += (state) =>
                {
                    _logger.LogInformation("{onicegatheringstatechange}");
                };

                peerConnection.OnSendReport += (media, sr) =>
                {
                    _logger.LogInformation("{OnSendReport}");
                };

                peerConnection.OnReceiveReport += (arg1, media, sr) =>
                {
                    _logger.LogInformation("{OnReceiveReport}");
                };

                peerConnection.OnRtcpBye += (reason) =>
                {
                    _logger.LogInformation("{OnRtcpBye}");
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
                    _logger.LogInformation("{onconnectionstatechange}");
                    if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                    {
                        _logger.LogInformation("Peer connection failed | closed | disconected");
                        _peerConnections.TryRemove(peerConnection.SessionID, out _);
                    }
                    else if (state == RTCPeerConnectionState.connected)
                    {
                        _logger.LogInformation("Peer connection connected.");
                    }
                };

                peerConnection.OnRtpPacketReceived += (rep, media, pkt) =>
                {
                    if (media == SDPMediaTypesEnum.audio)
                    {
                        var conns = _peerConnections.Where(p => p.Key != peerConnection.SessionID).Select(s => s.Value);
                        foreach (var pc in conns)
                        {
                            if (media == SDPMediaTypesEnum.audio && _audioSet[pc.SessionID] == false)
                            {
                                pc.SendRtpRaw(SDPMediaTypesEnum.audio, pkt.Payload, pkt.Header.Timestamp, pkt.Header.MarkerBit, pkt.Header.PayloadType);
                            }
                        }
                    }
                };

                await peerConnection.createDataChannel("channel");

                var offerSdp = peerConnection.createOffer(null);

                await peerConnection.setLocalDescription(offerSdp);

                _audioSet.Add(peerConnection.SessionID, true);
                _peerConnections.TryAdd(peerConnection.SessionID, peerConnection);

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
    }
}
