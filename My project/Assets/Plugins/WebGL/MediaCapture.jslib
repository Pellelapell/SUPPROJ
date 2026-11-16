// Partage d'écran (source 0) et caméra (source 1) dans le navigateur, pour MediaCapture.cs et ClassStream.cs.
//
// Capture : le flux est lu dans une balise <video> cachée. États : 0 arrêté, 1 en attente d'autorisation, 2 en direct,
// 3 refusé / erreur. MediaCapture_Grab encode l'image actuelle en JPEG (envoi de secours par le serveur du jeu).
//
// Diffusion WebRTC (vraie vidéo, sans délai) : l'organisateur ouvre une connexion directe avec chaque élève assis,
// avec deux pistes vidéo (écran, caméra) remplacées à la volée quand il démarre / arrête un partage.
// La négociation (offre, réponse, candidats réseau) passe par le jeu : Rtc_NextSignal donne les messages à envoyer
// ("idDestinataire|json"), Rtc_Receive reçoit ceux des autres. Media_UpdateTexture copie l'image vidéo directement
// dans une texture Unity.
var MediaCaptureLib = {
    $MediaCaptureData: {
        sources: [null, null],
        peers: {},     // id du joueur -> { pc, presenter, videos: [écran, caméra] }
        outbox: [],
        iceServers: [{ urls: 'stun:stun.l.google.com:19302' }],
        // Débit max par élève : écran net mais pas trop lourd, caméra légère
        maxBitrate: [900000, 300000],

        track: function (source) {
            var s = MediaCaptureData.sources[source];
            return s && s.state == 2 && s.stream ? s.stream.getVideoTracks()[0] : null;
        },

        send: function (to, message) {
            MediaCaptureData.outbox.push(to + '|' + JSON.stringify(message));
        },

        // Organisateur : pistes actuelles sur toutes les connexions (sans renégocier)
        updateTracks: function () {
            for (var id in MediaCaptureData.peers) {
                var peer = MediaCaptureData.peers[id];
                if (!peer.presenter) continue;
                peer.pc.getTransceivers().forEach(function (t, i) {
                    if (i < 2) t.sender.replaceTrack(MediaCaptureData.track(i)).catch(function () {});
                });
            }
        },

        createPeer: function (id, presenter) {
            MediaCaptureData.closePeer(id);
            var pc = new RTCPeerConnection({ iceServers: MediaCaptureData.iceServers });
            var peer = { pc: pc, presenter: presenter, videos: [null, null] };
            MediaCaptureData.peers[id] = peer;

            pc.onicecandidate = function (e) {
                if (e.candidate) MediaCaptureData.send(id, { type: 'candidate', candidate: e.candidate });
            };
            pc.onconnectionstatechange = function () {
                if (pc.connectionState == 'failed' || pc.connectionState == 'closed') MediaCaptureData.closePeer(id);
            };
            pc.ontrack = function (e) {
                var index = pc.getTransceivers().indexOf(e.transceiver);
                if (index < 0 || index > 1) return;
                var video = document.createElement('video');
                video.muted = true;
                video.playsInline = true;
                video.srcObject = new MediaStream([e.track]);
                video.play().catch(function () {});
                peer.videos[index] = video;
            };
            return peer;
        },

        closePeer: function (id) {
            var peer = MediaCaptureData.peers[id];
            if (!peer) return;
            delete MediaCaptureData.peers[id];
            try { peer.pc.close(); } catch (e) {}
        },

        limitBitrate: function (pc) {
            pc.getTransceivers().forEach(function (t, i) {
                if (i > 1) return;
                var params = t.sender.getParameters();
                if (!params.encodings || params.encodings.length == 0) params.encodings = [{}];
                params.encodings[0].maxBitrate = MediaCaptureData.maxBitrate[i];
                t.sender.setParameters(params).catch(function () {});
            });
        },

        // Vidéo à afficher : flux local (organisateur) ou reçu (élève), avec une image prête
        video: function (source, local) {
            if (local) {
                var s = MediaCaptureData.sources[source];
                return s && s.state == 2 ? s.video : null;
            }
            for (var id in MediaCaptureData.peers) {
                var v = MediaCaptureData.peers[id].videos[source];
                if (v && v.videoWidth > 0 && v.readyState >= 2) return v;
            }
            return null;
        }
    },

    // ---------- Capture ----------

    MediaCapture_Start: function (source) {
        var current = MediaCaptureData.sources[source];
        if (current && (current.state == 1 || current.state == 2)) return;

        var s = { state: 1, stream: null, video: null, canvas: document.createElement('canvas') };
        MediaCaptureData.sources[source] = s;

        var devices = navigator.mediaDevices;
        if (!devices || !(source == 0 ? devices.getDisplayMedia : devices.getUserMedia)) {
            s.state = 3;
            return;
        }
        var request = source == 0
            ? devices.getDisplayMedia({ video: { frameRate: 15 }, audio: false })
            : devices.getUserMedia({ video: { width: 640, height: 480, frameRate: 24 }, audio: false });

        request.then(function (stream) {
            // Arrêté pendant la demande d'autorisation
            if (MediaCaptureData.sources[source] !== s) {
                stream.getTracks().forEach(function (t) { t.stop(); });
                return;
            }
            s.stream = stream;
            var track = stream.getVideoTracks()[0];
            // Écran : priorité à la netteté du texte plutôt qu'à la fluidité
            if (source == 0 && 'contentHint' in track) track.contentHint = 'detail';
            s.video = document.createElement('video');
            s.video.muted = true;
            s.video.playsInline = true;
            s.video.srcObject = stream;
            s.video.play().catch(function () {});
            // Arrêt depuis le bandeau "Arrêter le partage" du navigateur
            track.addEventListener('ended', function () {
                s.state = 0;
                MediaCaptureData.updateTracks();
            });
            s.state = 2;
            MediaCaptureData.updateTracks();
        }).catch(function (e) {
            console.warn('MediaCapture', e);
            s.state = 3;
        });
    },

    MediaCapture_Stop: function (source) {
        var s = MediaCaptureData.sources[source];
        if (s && s.stream) s.stream.getTracks().forEach(function (t) { t.stop(); });
        MediaCaptureData.sources[source] = null;
        MediaCaptureData.updateTracks();
    },

    MediaCapture_State: function (source) {
        var s = MediaCaptureData.sources[source];
        return s ? s.state : 0;
    },

    MediaCapture_Grab: function (source, maxWidth, quality, buffer, size) {
        var s = MediaCaptureData.sources[source];
        if (!s || s.state != 2 || !s.video.videoWidth) return 0;

        var width = Math.min(maxWidth, s.video.videoWidth);
        var height = Math.round(width * s.video.videoHeight / s.video.videoWidth);
        s.canvas.width = width;
        s.canvas.height = height;
        s.canvas.getContext('2d').drawImage(s.video, 0, 0, width, height);

        var url = s.canvas.toDataURL('image/jpeg', quality);
        var bytes = atob(url.substring(url.indexOf(',') + 1));
        if (bytes.length > size) return 0;
        for (var i = 0; i < bytes.length; i++) HEAPU8[buffer + i] = bytes.charCodeAt(i);
        return bytes.length;
    },

    // ---------- Affichage dans une texture Unity ----------

    // Taille de la vidéo (largeur * 65536 + hauteur), 0 si aucune image
    Media_VideoSize: function (source, local) {
        var v = MediaCaptureData.video(source, local);
        return v ? v.videoWidth * 65536 + v.videoHeight : 0;
    },

    // Copie l'image actuelle dans la texture (créée par Unity à la taille de la vidéo). 1 si une image a été copiée.
    Media_UpdateTexture__deps: ['$GL'],
    Media_UpdateTexture: function (source, local, texture, width, height) {
        var v = MediaCaptureData.video(source, local);
        if (!v || v.videoWidth != width || v.videoHeight != height) return 0;
        // Même image que la dernière fois : la texture est déjà à jour
        if (v.unityTexture === texture && v.unityTime === v.currentTime) return 1;
        v.unityTexture = texture;
        v.unityTime = v.currentTime;
        var gl = GLctx;
        var previous = gl.getParameter(gl.TEXTURE_BINDING_2D);
        gl.bindTexture(gl.TEXTURE_2D, GL.textures[texture]);
        // Vidéo : origine en haut à gauche ; texture Unity : en bas à gauche
        gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true);
        gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, gl.RGBA, gl.UNSIGNED_BYTE, v);
        gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
        gl.bindTexture(gl.TEXTURE_2D, previous);
        return 1;
    },

    // ---------- Diffusion WebRTC ----------

    Rtc_Supported: function () {
        return typeof RTCPeerConnection !== 'undefined' ? 1 : 0;
    },

    // Message de négociation à envoyer ("idDestinataire|json"), ou null
    Rtc_NextSignal: function () {
        if (MediaCaptureData.outbox.length == 0) return null;
        var text = MediaCaptureData.outbox.shift();
        var size = lengthBytesUTF8(text) + 1;
        var buffer = _malloc(size);
        stringToUTF8(text, buffer, size);
        return buffer;
    },

    // Message de négociation reçu d'un autre joueur
    Rtc_Receive: function (fromPtr, jsonPtr) {
        var from = UTF8ToString(fromPtr);
        var message;
        try { message = JSON.parse(UTF8ToString(jsonPtr)); } catch (e) { return; }
        var peer = MediaCaptureData.peers[from];

        if (message.type == 'join') {
            // Organisateur : un élève vient de s'asseoir, on lui propose les deux pistes vidéo
            var p = MediaCaptureData.createPeer(from, true).pc;
            p.addTransceiver(MediaCaptureData.track(0) || 'video', { direction: 'sendonly' });
            p.addTransceiver(MediaCaptureData.track(1) || 'video', { direction: 'sendonly' });
            p.createOffer()
                .then(function (offer) { return p.setLocalDescription(offer); })
                .then(function () { MediaCaptureData.send(from, { type: 'offer', sdp: p.localDescription.sdp }); })
                .catch(function (e) { console.warn('Rtc offer', e); });
        } else if (message.type == 'offer') {
            // Élève : réponse à l'organisateur
            var r = MediaCaptureData.createPeer(from, false).pc;
            r.setRemoteDescription({ type: 'offer', sdp: message.sdp })
                .then(function () { return r.createAnswer(); })
                .then(function (answer) { return r.setLocalDescription(answer); })
                .then(function () { MediaCaptureData.send(from, { type: 'answer', sdp: r.localDescription.sdp }); })
                .catch(function (e) { console.warn('Rtc answer', e); });
        } else if (message.type == 'answer' && peer) {
            peer.pc.setRemoteDescription({ type: 'answer', sdp: message.sdp })
                .then(function () { MediaCaptureData.limitBitrate(peer.pc); })
                .catch(function (e) { console.warn('Rtc answer', e); });
        } else if (message.type == 'candidate' && peer) {
            peer.pc.addIceCandidate(message.candidate).catch(function () {});
        } else if (message.type == 'leave') {
            MediaCaptureData.closePeer(from);
        }
    },

    // Élève : 1 si une connexion directe avec un organisateur est établie
    Rtc_Connected: function () {
        for (var id in MediaCaptureData.peers) {
            var peer = MediaCaptureData.peers[id];
            if (!peer.presenter && peer.pc.connectionState == 'connected') return 1;
        }
        return 0;
    },

    // Élève qui se lève : ferme les connexions où il regarde (pas celles où il diffuse, s'il est aussi organisateur)
    Rtc_CloseViewing: function () {
        for (var id in MediaCaptureData.peers)
            if (!MediaCaptureData.peers[id].presenter) MediaCaptureData.closePeer(id);
    }
};

autoAddDeps(MediaCaptureLib, '$MediaCaptureData');
mergeInto(LibraryManager.library, MediaCaptureLib);
