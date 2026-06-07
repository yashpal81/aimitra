window.METASYNAPSEChat = (function () {
    let connection = null;
    let dotNetRef = null;
    let sessionCollection = null;
    const storageKey = 'METASYNAPSE.session.collection';

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
            const newCollection = collection || getOrCreateSessionCollection('chat');

            console.debug('METASYNAPSEChat.start called, new collection=', newCollection, 'current collection=', sessionCollection);
            
            // If switching to a different session, close the old connection first
            if (connection && sessionCollection && newCollection !== sessionCollection) {
                console.debug('Session collection changed from', sessionCollection, 'to', newCollection, '- closing old connection');
                const oldConnection = connection;
                connection = null;
                sessionCollection = null;
                oldConnection.stop().then(function () {
                    console.debug('Old connection closed successfully');
                }).catch(function (err) {
                    console.error('Error closing old connection', err);
                });
            }

            sessionCollection = newCollection;

            if (connection) {
                console.debug('Existing SignalR connection found for same collection, updating session.');
                return connection.invoke('SetSessionCollection', sessionCollection);
            }

            console.debug('Creating new SignalR connection');
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
                    console.error('SignalR start failed', err);
                });
        },
        stop: function () {
            if (connection) {
                console.debug('METASYNAPSEChat.stop called');
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

