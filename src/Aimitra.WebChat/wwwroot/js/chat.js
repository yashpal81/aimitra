window.aimitraChat = (function () {
    let connection = null;
    let dotNetRef = null;

    return {
        start: function (dotNetObject) {
            dotNetRef = dotNetObject;
            connection = new signalR.HubConnectionBuilder()
                .withUrl('/chathub')
                .withAutomaticReconnect()
                .build();

            connection.on('ReceiveMessage', function (user, message) {
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('ReceiveMessage', user, message);
                }
            });

            connection.start().catch(function (err) {
                console.error(err.toString());
            });
        },
        sendMessage: function (user, message) {
            if (connection) {
                connection.invoke('SendMessage', user, message).catch(function (err) {
                    console.error(err.toString());
                });
            }
        }
    };
})();
