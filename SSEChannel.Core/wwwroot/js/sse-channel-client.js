/**
 * SseChannelClient - A lightweight browser client for the SSEChannel system.
 *
 * Usage:
 *   const client = new SseChannelClient('/sse/my-channel');
 *   client.on('message', e => console.log(e.data));
 *   client.on('error', err => console.error(err));
 *   client.connect();
 *
 *   // Publish from the browser (client → server → all subscribers):
 *   await client.send({ text: 'Hello!' });
 *
 *   // Disconnect:
 *   client.disconnect();
 */
class SseChannelClient {
    /**
     * @param {string} channelPath - Base path such as '/sse/my-channel'
     * @param {object} [options]
     * @param {number} [options.reconnectDelayMs=3000] - Reconnect delay in ms
     * @param {number} [options.maxReconnectDelayMs=30000] - Max reconnect back-off
     * @param {boolean} [options.autoReconnect=true] - Reconnect automatically on close
     */
    constructor(channelPath, options = {}) {
        this._channelPath = channelPath;
        this._opts = {
            reconnectDelayMs: 3000,
            maxReconnectDelayMs: 30000,
            autoReconnect: true,
            ...options,
        };
        this._handlers = {};
        this._es = null;
        this._lastEventId = null;
        this._reconnectAttempt = 0;
        this._reconnectTimer = null;
        this._stopped = false;
    }

    /** Register an event handler. Use eventName='message' for the default event. */
    on(eventName, handler) {
        (this._handlers[eventName] ??= []).push(handler);
        return this;
    }

    /** Remove a previously registered handler. */
    off(eventName, handler) {
        if (!this._handlers[eventName]) return this;
        this._handlers[eventName] = this._handlers[eventName].filter(h => h !== handler);
        return this;
    }

    /** Open the SSE connection. */
    connect() {
        this._stopped = false;
        this._openEventSource();
        return this;
    }

    /** Close the SSE connection and stop auto-reconnect. */
    disconnect() {
        this._stopped = true;
        clearTimeout(this._reconnectTimer);
        if (this._es) {
            this._es.close();
            this._es = null;
        }
    }

    /**
     * Send a message from this client to all channel subscribers via the /send endpoint.
     * @param {*} payload - Any JSON-serializable value.
     * @param {string} [eventName] - Optional event name override.
     */
    async send(payload, eventName) {
        const body = {
            message: typeof payload === 'string' ? payload : JSON.stringify(payload),
            ...(eventName ? { eventName } : {}),
        };
        const resp = await fetch(`${this._channelPath}/send`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({ error: resp.statusText }));
            throw new Error(err.error ?? 'Failed to send message');
        }
        return resp.json();
    }

    /**
     * Fetch channel stats (connected client count).
     */
    async stats() {
        const resp = await fetch(`${this._channelPath}/stats`);
        if (!resp.ok) throw new Error(`Stats request failed: ${resp.statusText}`);
        return resp.json();
    }

    // ── Private ────────────────────────────────────────────────────────────────

    _openEventSource() {
        // EventSource automatically sends the Last-Event-ID header on reconnect,
        // so we always use the base channel path.
        this._es = new EventSource(this._channelPath);

        this._es.onopen = () => {
            this._reconnectAttempt = 0;
            this._emit('open', { type: 'open' });
        };

        this._es.onerror = (ev) => {
            this._emit('error', ev);
            if (!this._stopped && this._opts.autoReconnect) {
                this._es.close();
                this._es = null;
                this._scheduleReconnect();
            }
        };

        // Register all user-supplied event handlers
        for (const [eventName, handlers] of Object.entries(this._handlers)) {
            if (eventName === 'open' || eventName === 'error') continue;
            this._es.addEventListener(eventName, (ev) => {
                if (ev.lastEventId) this._lastEventId = ev.lastEventId;
                for (const h of handlers) {
                    try { h(ev); } catch (e) { console.error('[SseChannelClient] handler error', e); }
                }
            });
        }

        // Default 'message' listener (fires for events without an explicit 'event:' field)
        this._es.onmessage = (ev) => {
            if (ev.lastEventId) this._lastEventId = ev.lastEventId;
            this._emit('message', ev);
        };
    }

    _scheduleReconnect() {
        const delay = Math.min(
            this._opts.reconnectDelayMs * Math.pow(1.5, this._reconnectAttempt),
            this._opts.maxReconnectDelayMs,
        );
        this._reconnectAttempt++;
        this._reconnectTimer = setTimeout(() => {
            if (!this._stopped) this._openEventSource();
        }, delay);
    }

    _emit(eventName, event) {
        for (const h of (this._handlers[eventName] ?? [])) {
            try { h(event); } catch (e) { console.error('[SseChannelClient] handler error', e); }
        }
    }
}

// CommonJS / ES module dual export for non-browser environments
if (typeof module !== 'undefined' && module.exports) {
    module.exports = SseChannelClient;
}
