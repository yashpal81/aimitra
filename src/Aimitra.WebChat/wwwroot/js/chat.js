window.aimitraChat = (function () {
    let connection = null;
    let dotNetRef = null;
    let sessionCollection = null;

    return {
        start: function (dotNetObject, collection) {
            dotNetRef = dotNetObject;
            sessionCollection = collection || null;
            connection = new signalR.HubConnectionBuilder()
                .withUrl('/chathub')
                .withAutomaticReconnect()
                .build();

            connection.on('ReceiveMessage', function (user, message) {
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('ReceiveMessage', user, message);
                }
            });

            connection.start()
                .then(function () {
                    if (sessionCollection) {
                        return connection.invoke('SetSessionCollection', sessionCollection);
                    }
                })
                .catch(function (err) {
                    console.error(err.toString());
                });
        },
        sendMessage: function (user, message) {
            if (connection) {
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('ReceiveMessage', user, message);
                }
                connection.invoke('SendMessage', user, message).catch(function (err) {
                    console.error(err.toString());
                });
            }
        }
    };
})();
