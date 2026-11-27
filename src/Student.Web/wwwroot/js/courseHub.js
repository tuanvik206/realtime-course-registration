const recordSeparator = String.fromCharCode(0x1e);
const connections = new Map();

export function startCourseHub(connectionId, dotNet, courseIds) {
    stopCourseHub(connectionId);
    const connection = createCourseHub(dotNet);
    connections.set(connectionId, connection);
    connection.start(courseIds);
}

export function setCourseIds(connectionId, courseIds) {
    connections.get(connectionId)?.setCourses(courseIds);
}

export function stopCourseHub(connectionId) {
    const connection = connections.get(connectionId);
    if (!connection) return;
    connections.delete(connectionId);
    connection.stop();
}

function createCourseHub(dotNet) {
    let socket = null;
    let disposed = false;
    let connected = false;
    let handshakeComplete = false;
    let buffer = "";
    let nextInvocationId = 1;
    let currentCourses = new Set();
    let desiredCourses = new Set();
    let connectionNumber = 0;
    let retryDelay = 1000;

    const invoke = (target, args) => {
        if (!socket || socket.readyState !== WebSocket.OPEN || !handshakeComplete) return;
        socket.send(JSON.stringify({ type: 1, invocationId: String(nextInvocationId++), target, arguments: args }) + recordSeparator);
    };

    const syncSubscriptions = () => {
        if (!connected) return;
        for (const id of currentCourses) {
            if (!desiredCourses.has(id)) invoke("UnsubscribeCourse", [id]);
        }
        for (const id of desiredCourses) {
            if (!currentCourses.has(id)) invoke("SubscribeCourse", [id]);
        }
        currentCourses = new Set(desiredCourses);
    };

    const notifyState = state => dotNet.invokeMethodAsync("OnConnectionStateChanged", state).catch(() => {});

    const handleMessage = message => {
        if (!handshakeComplete) {
            if (message?.error) {
                socket?.close();
                return;
            }
            handshakeComplete = true;
            connected = true;
            currentCourses.clear();
            syncSubscriptions();
            connectionNumber++;
            retryDelay = 1000;
            notifyState("Connected");
            dotNet.invokeMethodAsync("OnReconnected").catch(() => {});
            return;
        }

        if (!message || typeof message !== "object") return;
        if (message.type === 1) {
            const args = Array.isArray(message.arguments) ? message.arguments : [];
            if (message.target === "CourseCapacityUpdated" && args[0]) {
                dotNet.invokeMethodAsync("OnCapacityUpdated", args[0]).catch(() => {});
            } else if (message.target === "CourseCatalogChanged" && args[0]) {
                dotNet.invokeMethodAsync("OnCatalogChanged", args[0].courseSectionId).catch(() => {});
            }
        } else if (message.type === 7) {
            socket?.close();
        }
    };

    const openOnce = async () => {
        const hubUrl = new URL("/hubs/courses", window.location.href);
        const negotiateUrl = new URL(`${hubUrl.pathname}/negotiate`, hubUrl.origin);
        negotiateUrl.searchParams.set("negotiateVersion", "1");
        const response = await fetch(negotiateUrl, {
            method: "POST",
            credentials: "same-origin",
            cache: "no-store"
        });
        if (disposed) return;
        if (!response.ok) throw new Error(`hub negotiate ${response.status}`);
        const negotiation = await response.json();
        if (!negotiation.connectionToken) throw new Error("hub negotiation response missing connection token");

        hubUrl.protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
        hubUrl.searchParams.set("id", negotiation.connectionToken);
        buffer = "";
        handshakeComplete = false;
        socket = new WebSocket(hubUrl);
        await new Promise((resolve, reject) => {
            socket.addEventListener("open", () => {
                socket.send(JSON.stringify({ protocol: "json", version: 1 }) + recordSeparator);
                resolve();
            }, { once: true });
            socket.addEventListener("error", () => reject(new Error("hub websocket failed")), { once: true });
            socket.addEventListener("close", () => reject(new Error("hub websocket closed before opening")), { once: true });
        });
        if (disposed) {
            socket.close(1000, "component disposed");
            return;
        }

        socket.addEventListener("message", event => {
            buffer += typeof event.data === "string" ? event.data : "";
            const frames = buffer.split(recordSeparator);
            buffer = frames.pop() ?? "";
            for (const frame of frames) {
                if (!frame) continue;
                try { handleMessage(JSON.parse(frame)); }
                catch (error) { console.warn("Ignoring invalid SignalR frame", error); }
            }
        });
        await new Promise(resolve => socket.addEventListener("close", resolve, { once: true }));
    };

    const run = async () => {
        while (!disposed) {
            notifyState(connectionNumber === 0 ? "Connecting" : "Reconnecting");
            try { await openOnce(); }
            catch (error) { console.warn("Course SignalR connection unavailable", error); }
            connected = false;
            currentCourses.clear();
            if (disposed) break;
            notifyState("Reconnecting");
            await new Promise(resolve => setTimeout(resolve, retryDelay));
            retryDelay = Math.min(retryDelay * 2, 15000);
        }
        notifyState("Disconnected");
    };

    return {
        start(courseIds) {
            desiredCourses = new Set(courseIds ?? []);
            run();
        },
        setCourses(courseIds) {
            desiredCourses = new Set(courseIds ?? []);
            syncSubscriptions();
        },
        stop() {
            disposed = true;
            socket?.close(1000, "component disposed");
        }
    };
}
