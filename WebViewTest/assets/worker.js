const chan = new BroadcastChannel("worker-channel")
let count = 0;

setInterval(() => {
    count++;
    chan.postMessage({ count });
}, 10000)
