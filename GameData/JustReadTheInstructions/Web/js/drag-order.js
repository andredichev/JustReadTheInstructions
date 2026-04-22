const DRAG_THRESHOLD = 5;

export function enableDragOrder(container, onReorder) {
    let drag = null;

    const onMove = (e) => {
        if (!drag) return;
        if (!drag.active) {
            if (Math.hypot(e.clientX - drag.startX, e.clientY - drag.startY) < DRAG_THRESHOLD) return;
            drag.active = true;
            _activate(drag, e);
        }
        drag.card.style.left = `${e.clientX - drag.offsetX}px`;
        drag.card.style.top = `${e.clientY - drag.offsetY}px`;
        _movePlaceholder(container, drag.placeholder, e.clientX, e.clientY);
    };

    const onUp = () => {
        if (!drag) return;
        if (drag.active) {
            drag.card.removeAttribute('style');
            drag.placeholder.replaceWith(drag.card);
            onReorder();
        }
        window.removeEventListener('pointermove', onMove);
        window.removeEventListener('pointerup', onUp);
        window.removeEventListener('pointercancel', onUp);
        drag = null;
    };

    container.addEventListener('pointerdown', (e) => {
        if (e.button != null && e.button !== 0) return;
        const card = e.target.closest('.camera-card');
        if (!card || !container.contains(card)) return;
        if (e.target.closest('button, a, input, select')) return;
        if (e.pointerType === 'touch' && !e.target.closest('.preview')) return;

        drag = {
            card,
            placeholder: null,
            startX: e.clientX,
            startY: e.clientY,
            offsetX: 0,
            offsetY: 0,
            active: false,
        };
        window.addEventListener('pointermove', onMove);
        window.addEventListener('pointerup', onUp);
        window.addEventListener('pointercancel', onUp);
    });
}

function _activate(drag, e) {
    const rect = drag.card.getBoundingClientRect();
    drag.offsetX = e.clientX - rect.left;
    drag.offsetY = e.clientY - rect.top;

    const ph = document.createElement('div');
    ph.className = 'drag-placeholder';
    ph.style.cssText = `width:${rect.width}px;height:${rect.height}px`;
    drag.card.replaceWith(ph);
    drag.placeholder = ph;

    drag.card.style.cssText = [
        `position:fixed`,
        `left:${rect.left}px`,
        `top:${rect.top}px`,
        `width:${rect.width}px`,
        `height:${rect.height}px`,
        `z-index:1000`,
        `opacity:0.88`,
        `pointer-events:none`,
        `box-shadow:0 12px 40px rgba(0,0,0,0.6)`,
        `transform:rotate(1.5deg)`,
    ].join(';');
    document.body.appendChild(drag.card);
}

function _movePlaceholder(container, placeholder, cx, cy) {
    const cards = [...container.querySelectorAll('.camera-card')];
    if (!cards.length) return;

    let best = null;
    let bestDist = Infinity;
    for (const c of cards) {
        const r = c.getBoundingClientRect();
        const d = Math.hypot(cx - (r.left + r.width / 2), cy - (r.top + r.height / 2));
        if (d < bestDist) { bestDist = d; best = c; }
    }

    if (!best) return;
    const r = best.getBoundingClientRect();
    const midY = r.top + r.height / 2;
    const midX = r.left + r.width / 2;
    const insertBefore = cy < midY - r.height * 0.1
        || (Math.abs(cy - midY) <= r.height * 0.1 && cx <= midX);
    insertBefore ? best.before(placeholder) : best.after(placeholder);
}
