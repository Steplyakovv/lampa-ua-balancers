(function () {
    'use strict';

    const PLUGIN_ID = 'tmdb_stream';
    //const DEFAULT_BACKEND = 'https://localhost:7224';
    const DEFAULT_BACKEND = 'https://lampa-ua-balancers.onrender.com';

    const CONFIG = {
        apiBase: DEFAULT_BACKEND,
        buttonClass: 'button--uafix',
        pluginName: 'Uafix Play',
        button: `<svg height="24" viewBox="0 0 24 24" width="24" fill="none" xmlns="http://www.w3.org">
                    <path d="M8 5v14l11-7z" fill="currentColor" />
                </svg>`
    };

    const VideoType = Object.freeze({
        Film: 0,
        Serial: 1,
        Episode: 2
    });

    const FolderType = Object.freeze({
        VoiceActing: 0,
        Season: 1,
        Episode: 2
    });

    function getBackend() {
        return Lampa.Storage.get(PLUGIN_ID + '_backend', DEFAULT_BACKEND);
    }

    function setBackend(value) {
        Lampa.Storage.set(PLUGIN_ID + '_backend', value);
    }

    function checkStatus() {
        const url = getBackend() + '/api/status';
        Lampa.Noty.show('Перевірка зв’язку з сервером...');

        fetch(url)
            .then(r => r.ok ? r.json() : Promise.reject(r))
            .then(data => {
                Lampa.Noty.show('✅ Сервер: ' + (data.status || 'Працює'));
            })
            .catch(() => {
                Lampa.Noty.show('❌ Сервер недоступний');
            });
    }

    function renderButton(e) {
        const render = e.object.activity ? e.object.activity.render() : e.container;
        const buttonsGroup = render.find('.full-start-new__buttons');

        if (!buttonsGroup.length || buttonsGroup.find(`.${CONFIG.buttonClass}`).length)
            return;

        const button = $(`
            <div class="full-start__button selector ${CONFIG.buttonClass}">
                    ${CONFIG.button}
                <span>${CONFIG.pluginName}</span>
            </div>
        `);

        button.on('hover:enter', () => handleAction(e));

        buttonsGroup.append(button);
    }

    async function handleAction(e) {
        const movie = (e.data || e.object.data).movie;

        Lampa.Noty.show(`Пошук контенту...`);

        try {
            const videoType = getTypeVideo(movie);
            const data = await fetchStream(movie, videoType);

            if (data && data.searchResults && data.searchResults.length) {
                Lampa.Select.show({
                    title: 'Оберіть варіант',
                    items: data.searchResults.map(item => ({
                        title: item.title,
                        icon: '<i class="fa fa-film"></i>',
                        url: item.url
                    })),
                    onSelect: async (selectedItem) => {
                        Lampa.Noty.show('Отримуємо пряме посилання...');

                        const streamData = await fetchToExtractStream(selectedItem.url, videoType);

                        playVideo(streamData, selectedItem.title, movie, videoType);
                    },
                    onBack: () => {
                        Lampa.Controller.toggle('content');
                    }
                });
                return;
            }

            playVideo(data, data.title || movie.title, movie, videoType);
        } catch (error) {
            Lampa.Noty.show('Помилка: ' + error.message);
            console.error('[Uafix Error]', error);
        }
    }

    function getTypeVideo(movie) {
        return (movie.number_of_seasons || movie.seasons)
            ? VideoType.Serial
            : VideoType.Film;
    }

    function playVideo(data, title, movie, videoType) {
        if (videoType === VideoType.Serial) {
            playSerial(data, 'Оберить озвучку або сезон')
        } else {
            playMovie(data, title, movie);
        }
    }

    function playMovie(data, title, movie) {
        if (data && data.url && data.success) {
            startPlayback(data.url, title);
            Lampa.Player.playlist([]);
        } else {
            Lampa.Noty.show(GetMessageByStatusCode(data));
        }
    }

    function playSerial(data, title) {
        if (data && data.serial && data.success) {
            startSerialPlayback(data.serial, title);
        } else {
            Lampa.Noty.show(GetMessageByStatusCode(data));
        }
    }

    function GetMessageByStatusCode(data) {
        const statusMessages = {
            403: '🚫 Не маємо доступу до цього розділу',
            404: '🔍 Фільм не знайдено',
            500: '🖥️ Помилка сервера проксі',
            429: '⏳ Занадто багато запитів'
        };

        return statusMessages[data.status]
            || data.message
            || `⚠️ Помилка завантаження (${data.status || '???'})`;
    }

    async function fetchToExtractStream(url, videoType) {
        const extractUrl = `${CONFIG.apiBase}/extract?url=${encodeURIComponent(url)}&videoType=${videoType}`;

        try {
            const response = await fetch(extractUrl);

            if (!response.ok) {
                const errorData = await response.json().catch(() => ({}));

                return {
                    success: false,
                    status: response.status,
                    message: errorData.message || 'Помилка сервера'
                };
            }
            
            return await response.json();
        } catch (e) {
            return {
                success: false,
                status: 500,
                message: 'Нема звязку з сервером'
            };
        }
    }

    async function fetchStream(movie, videoType) {
        const rawTitles = [movie.original_title, movie.title, movie.name]
            .filter(Boolean)
            .map(t => t.replace(/[:"'»«]/g, '').replace(/\s+/g, ' ').trim());

        const titles = [...new Set(rawTitles)];
        const params = titles.map(t => `titles=${encodeURIComponent(t)}`).join('&');
        const url = `${CONFIG.apiBase}/find-stream?${params}&videoType=${videoType}`;

        try {
            const response = await fetch(url);
            if (!response.ok) {
                const errorData = await response.json().catch(() => ({}));

                return {
                    success: false,
                    status: response.status,
                    message: errorData.message || 'Помилка сервера'
                };
            }

            return await response.json();
        } catch (e) {
            return {
                success: false,
                status: 500,
                message: 'Нема звязку з сервером'
            };
        }
    }

    function getProxyUrl(streamUrl) {
        return `${CONFIG.apiBase}/proxy-m3u8?url=${encodeURIComponent(streamUrl)}`
    }

    function startSerialPlayback(items, currentTitle, episodes) {
        let history = Lampa.Storage.get('uafix_history', []);
        let last = history[history.length - 1];

        if (!last || last.title !== currentTitle) {
            pushHistory(items, currentTitle, episodes);
        }

        Lampa.Select.show({
            title: currentTitle,
            items: items.map(i => ({
                title: i.title,
                subtitle: i.file ? '🎞️ Серія' : '📁 Папка',
                ...i
            })),
            onSelect: async (selected) => {
                if (selected.type === FolderType.Season && !selected.folder) {
                    Lampa.Noty.show('Завантаження серій...');

                    const data = await fetchToExtractStream(selected.auxiliaryLink, VideoType.Episode);

                    if (data && data.success && data.serial) {
                        selected.folder = data.serial;
                        startSerialPlayback(selected.folder, selected.title, selected.folder);
                    } else {
                        Lampa.Noty.show(GetMessageByStatusCode(data));
                    }

                    return;
                }

                if (selected.file) {
                    const playList = (episodes || items)
                        .filter(e => e.file)
                        .map(e => ({
                            url: e.file.includes('.m3u8') ? getProxyUrl(e.file) : e.file,
                            title: e.title
                        }));
                    
                    startPlayback(
                        selected.file,
                        selected.title,
                        () => {
                            popHistory();
                            startSerialPlayback(episodes, selected.title, episodes);
                        }
                    );
                    Lampa.Player.playlist(playList);
                } else if (selected.folder) {
                    startSerialPlayback(selected.folder, selected.title, selected.folder);
                }
            },
            onBack: () => {
                popHistory();
                const prev = popHistory();

                if (prev) {
                    startSerialPlayback(prev.items, prev.title, prev.episodes);
                } else {
                    Lampa.Controller.toggle('content');
                }
            }
        });
    }

    function pushHistory(items, title, episodes) {
        let history = Lampa.Storage.get('uafix_history', []);

        history.push({ items, title, episodes });
        Lampa.Storage.set('uafix_history', history);
    }

    function popHistory() {
        let history = Lampa.Storage.get('uafix_history', []);

        if (history.length === 0)
            return null;

        const last = history.pop();
        Lampa.Storage.set('uafix_history', history);

        return last;
    }

    function startPlayback(streamUrl, title, onBack) {
        if (streamUrl.startsWith('youtube:')) {
            const trailerBtn = $('.button--play').first();

            if (trailerBtn.length) {
                Lampa.Noty.show('Запуск трейлера...');
                trailerBtn.trigger('hover:enter');
            }

            return;
        }

        const finalUrl = streamUrl.includes('.m3u8')
            ? getProxyUrl(streamUrl)
            : streamUrl;

        Lampa.Player.play({
            title: title,
            url: finalUrl
        });

        Lampa.Player.callback(() => {
            if (onBack) onBack();
            else Lampa.Controller.toggle('content');
        });
    }

    function initPlugin() {
        if (!window.Lampa)
            return;

        Lampa.SettingsApi.addComponent({
            component: PLUGIN_ID,
            icon: CONFIG.button,
            name: 'Uafix Play'
        });

        Lampa.SettingsApi.addParam({
            component: PLUGIN_ID,
            field: {
                name: 'edit_backend',
                type: 'button'
            },
            param: {
                name: 'edit_backend',
                type: 'button',
                title: 'Редактировать Backend URL'
            },
            onChange: function () {
                const current = getBackend();
                const newVal = prompt('Введите Backend URL', current);
                if (newVal) {
                    setBackend(newVal);
                    Lampa.Noty.show('Сохранено: ' + newVal);
                }
            }
        });

        Lampa.SettingsApi.addParam({
            component: PLUGIN_ID,
            field: {
                name: 'check_status',
                type: 'button'
            },
            param: {
                name: 'check_status',
                type: 'button',
                title: 'Проверить backend'
            },
            onChange: checkStatus
        });

        Lampa.Listener.follow('full', (e) => {
            if (e.type === 'complite')
                renderButton(e);
        });
    }

    if (window.appready)
        initPlugin();
    else {
        Lampa.Listener.follow('app', function (event) {
            if (event.type === 'ready')
                initPlugin();
        });
    }

})();