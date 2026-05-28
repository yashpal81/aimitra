window.aimitraChat = (function () {
    let connection = null;
    let dotNetRef = null;
    let sessionCollection = null;
    const storageKey = 'aimitra.session.collection';

    function getOrCreateSessionCollection(prefix) {
        const keyPrefix = prefix || 'chat';
        let value = sessionStorage.getItem(storageKey);
        if (!value) {
            value = `${keyPrefix}-${crypto.randomUUID()}`;
            sessionStorage.setItem(storageKey, value);
        }
        return value;
    }

    return {
        getOrCreateSessionCollection: function (prefix) {
            return getOrCreateSessionCollection(prefix);
        },
        start: function (dotNetObject, collection) {
            dotNetRef = dotNetObject;
            sessionCollection = collection || getOrCreateSessionCollection('chat');

            if (connection) {
                return connection.invoke('SetSessionCollection', sessionCollection);
            }

            connection = new signalR.HubConnectionBuilder()
                .withUrl('/chathub')
                .withAutomaticReconnect()
                .build();

            connection.on('ReceiveMessage', function (user, message) {
                if (dotNetRef) {
                    const args = Array.prototype.slice.call(arguments);
                    dotNetRef.invokeMethodAsync(
                        'ReceiveMessage',
                        user,
                        message,
                        args.length > 2 ? args[2] : null,
                        args.length > 3 ? args[3] : false
                    );
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
        stop: function () {
            if (connection) {
                const current = connection;
                connection = null;
                return current.stop();
            }
        },
        sendMessage: function (user, message) {
            if (connection) {
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('ReceiveMessage', user, message, null, false);
                }
                connection.invoke('SendMessage', user, message).catch(function (err) {
                    console.error(err.toString());
                });
            }
        }
    };
})();
