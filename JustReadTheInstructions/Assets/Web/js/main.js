import { CAMERA_SYNC_MS } from './config.js';
import { fetchCameras } from './api.js';
import { CameraCard } from './camera-card.js';
import { mountSettingsUI } from './settings-ui.js';

const cards = new Map();

const liveContainer = document.getElementById('cameras-live');
const offlineSection = document.getElementById('cameras-offline-section');
const offlineContainer = document.getElementById('cameras-offline');

function setStatus(id, message) {
    const el = document.getElementById(id);
    if (!el) return;
    if (message) {
        el.textContent = message;
        el.classList.add('visible');
    } else {
        el.classList.remove('visible');
    }
}

async function sync() {
    let cameras;
    try {
        cameras = await fetchCameras();
        setStatus('error', null);
    } catch {
        setStatus('error', 'Could not connect to KSP. Is the game running?');
        setStatus('empty', null);
        return;
    }

    const incomingIds = new Set(cameras.map((c) => c.id));

    for (const [id, card] of cards) {
        if (!incomingIds.has(id) && !card.destroyed) {
            card.markDestroyed();
            offlineContainer.appendChild(card.el);
        }
    }

    for (const cam of cameras) {
        const existing = cards.get(cam.id);
        if (existing) {
            if (existing.destroyed) {
                existing.revive(cam);
                liveContainer.appendChild(existing.el);
            } else {
                existing.update(cam);
            }
        } else {
            const card = new CameraCard(cam);
            cards.set(cam.id, card);
            liveContainer.appendChild(card.el);
        }
    }

    const hasOffline = [...cards.values()].some(c => c.destroyed);
    offlineSection.hidden = !hasOffline;

    setStatus('empty', cameras.length === 0 && !hasOffline
        ? 'No cameras open. Open or stream a hull camera in KSP first.'
        : null);
}

function wireLifecycle() {
    const finalize = () => {
        for (const card of cards.values()) card.emergencyFinalize();
    };
    window.addEventListener('pagehide', finalize);
    window.addEventListener('beforeunload', finalize);
}

function main() {
    mountSettingsUI();
    wireLifecycle();
    sync();
    setInterval(sync, CAMERA_SYNC_MS);
}

main();