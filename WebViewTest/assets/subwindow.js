const chan = new BroadcastChannel("worker-channel")
chan.onmessage = (ev) => {
    document.getElementById("txt-received").textContent = JSON.stringify(ev.data)
}
