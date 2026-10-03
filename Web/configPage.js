const STRINGS = {
    ru: {
        'page.description': 'Настройте параметры подключения к вашему серверу Nextcloud для авторизации пользователей.',
        'field.serverUrl.label': 'URL сервера Nextcloud',
        'field.serverUrl.description': 'Базовый адрес вашего Nextcloud без слеша на конце.',
        'field.clientSecret.description': 'Оставьте пустым, чтобы сохранить текущий секрет без изменений.',
        'field.publicBaseUrl.label': 'Публичный базовый URL (необязательно)',
        'field.publicBaseUrl.description': 'Внешний адрес этого Jellyfin, используемый в redirect URI. Оставьте пустым для автоопределения.',
        'field.autoCreate.label': 'Автоматически создавать пользователей',
        'field.autoCreate.description': 'Новые пользователи из Nextcloud будут созданы в Jellyfin при первом входе.',
        'field.bindByName.label': 'Привязывать существующих пользователей по имени',
        'field.bindByName.description': 'Временная миграционная опция: при входе пользователь привязывается к аккаунту с совпадающим именем. После миграции отключите её — иначе возможен захват чужого аккаунта при совпадении имён.',
        'field.pkce.label': 'Использовать PKCE (S256)',
        'field.pkce.description': 'Включайте, только если приложение Nextcloud зарегистрировано с поддержкой PKCE.',
        'button.save': 'Сохранить',
        'section.login.title': 'Вход через Nextcloud',
        'section.login.description': 'Ссылка для входа пользователей через Nextcloud. Сохраните настройки перед использованием.',
        'button.copyLink': 'Копировать ссылку',
        'section.test.title': 'Тестирование подключения',
        'section.test.description': 'Проверьте корректность настроек, подключившись к Nextcloud. Предварительно сохраните настройки.',
        'button.testConnection': 'Проверить подключение',
        'error.loadFailed': 'Не удалось загрузить настройки',
        'error.saveFailed': 'Ошибка сохранения',
        'error.saveUnexpected': 'Не удалось сохранить настройки. Проверьте консоль.',
        'status.copied': 'Ссылка скопирована',
        'status.testing': 'Проверка подключения...',
        'status.success': 'Подключение успешно! Nextcloud версия: {0}',
        'status.unknown': 'неизвестно',
        'status.error': 'Ошибка: {0}',
        'status.errorUnknown': 'неизвестная ошибка',
        'status.connectionError': 'Ошибка подключения: {0}'
    },
    en: {
        'page.description': 'Configure the connection to your Nextcloud server to let users sign in with it.',
        'field.serverUrl.label': 'Nextcloud server URL',
        'field.serverUrl.description': 'The base address of your Nextcloud without a trailing slash.',
        'field.clientSecret.description': 'Leave empty to keep the current secret unchanged.',
        'field.publicBaseUrl.label': 'Public base URL (optional)',
        'field.publicBaseUrl.description': 'The external address of this Jellyfin used in the redirect URI. Leave empty to detect it automatically.',
        'field.autoCreate.label': 'Automatically create users',
        'field.autoCreate.description': 'New users from Nextcloud will be created in Jellyfin on their first sign-in.',
        'field.bindByName.label': 'Bind existing users by name',
        'field.bindByName.description': 'Temporary migration option: on sign-in the user is bound to the account with a matching name. Disable it after migrating — otherwise an account can be taken over when names collide.',
        'field.pkce.label': 'Use PKCE (S256)',
        'field.pkce.description': 'Enable only if the Nextcloud app is registered with PKCE support.',
        'button.save': 'Save',
        'section.login.title': 'Sign in with Nextcloud',
        'section.login.description': 'The sign-in link for your users. Save the settings before using it.',
        'button.copyLink': 'Copy link',
        'section.test.title': 'Connection test',
        'section.test.description': 'Verify your settings by connecting to Nextcloud. Save the settings first.',
        'button.testConnection': 'Test connection',
        'error.loadFailed': 'Failed to load settings',
        'error.saveFailed': 'Failed to save settings',
        'error.saveUnexpected': 'Could not save the settings. Check the browser console.',
        'status.copied': 'Link copied',
        'status.testing': 'Testing connection...',
        'status.success': 'Connection successful! Nextcloud version: {0}',
        'status.unknown': 'unknown',
        'status.error': 'Error: {0}',
        'status.errorUnknown': 'unknown error',
        'status.connectionError': 'Connection error: {0}'
    }
};

// Язык интерфейса Jellyfin: глобальный атрибут lang ставит globalize.updateCurrentCulture()
function resolveLocale() {
    const candidates = [
        document.documentElement.getAttribute('lang'),
        typeof navigator !== 'undefined' ? navigator.language : null
    ];

    for (const candidate of candidates) {
        if (!candidate) {
            continue;
        }
        const base = candidate.split('-')[0].toLowerCase();
        if (Object.prototype.hasOwnProperty.call(STRINGS, base)) {
            return base;
        }
    }

    return 'en';
}

function t(key, ...args) {
    const dict = STRINGS[resolveLocale()];
    let text = Object.prototype.hasOwnProperty.call(dict, key) ? dict[key] : key;
    for (let i = 0; i < args.length; i++) {
        text = text.split('{' + i + '}').join(String(args[i]));
    }
    return text;
}

function applyTranslations(root) {
    for (const element of root.querySelectorAll('[data-i18n]')) {
        element.textContent = t(element.getAttribute('data-i18n'));
    }
}

export default function (view) {
    console.log("[NextcloudOAuth2] Страница настроек инициализирована");

    applyTranslations(view);

    const form = view.querySelector('#NextcloudOAuth2Form');
    const inputServerUrl = view.querySelector('#txtServerUrl');
    const inputClientId = view.querySelector('#txtClientId');
    const inputClientSecret = view.querySelector('#txtClientSecret');
    const inputPublicBaseUrl = view.querySelector('#txtPublicBaseUrl');
    const chkAutoCreateUsers = view.querySelector('#chkAutoCreateUsers');
    const chkBindExistingUsersByName = view.querySelector('#chkBindExistingUsersByName');
    const chkEnablePkce = view.querySelector('#chkEnablePkce');
    const inputSsoLoginUrl = view.querySelector('#txtSsoLoginUrl');
    const btnCopySsoUrl = view.querySelector('#btnCopySsoUrl');
    const btnTestConnection = view.querySelector('#btnTestConnection');
    const connectionStatus = view.querySelector('#connectionStatus');

    inputSsoLoginUrl.value = ApiClient.getUrl('sso/OID/start/nextcloud');

    loadConfiguration();
    form.addEventListener('submit', onSave);
    btnCopySsoUrl.addEventListener('click', onCopySsoUrl);
    btnTestConnection.addEventListener('click', onTestConnection);

    async function loadConfiguration() {
        try {
            const response = await fetch(ApiClient.getUrl('NextcloudOAuth2/Configuration'), {
                method: 'GET',
                headers: { 'Authorization': 'MediaBrowser Token=' + ApiClient.accessToken() }
            });

            if (!response.ok) throw new Error(t('error.loadFailed'));

            const config = await response.json();

            inputServerUrl.value = config.NextcloudServerUrl || '';
            inputClientId.value = config.ClientId || '';
            // Секрет на клиент не приходит; поле остаётся пустым до ввода нового значения
            inputClientSecret.value = '';
            inputPublicBaseUrl.value = config.PublicBaseUrl || '';
            chkAutoCreateUsers.checked = config.AutoCreateUsers !== false;
            chkBindExistingUsersByName.checked = config.BindExistingUsersByName !== false;
            chkEnablePkce.checked = !!config.EnablePkce;

            console.log("[NextcloudOAuth2] Настройки загружены");
        } catch (err) {
            console.error("[NextcloudOAuth2] Ошибка загрузки:", err);
        }
    }

    async function onSave(e) {
        e.preventDefault();

        const newConfig = {
            NextcloudServerUrl: inputServerUrl.value.trim(),
            ClientId: inputClientId.value.trim(),
            ClientSecret: inputClientSecret.value,
            PublicBaseUrl: inputPublicBaseUrl.value.trim(),
            AutoCreateUsers: chkAutoCreateUsers.checked,
            BindExistingUsersByName: chkBindExistingUsersByName.checked,
            EnablePkce: chkEnablePkce.checked
        };

        try {
            const response = await fetch(ApiClient.getUrl('NextcloudOAuth2/Configuration'), {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'MediaBrowser Token=' + ApiClient.accessToken()
                },
                body: JSON.stringify(newConfig)
            });

            if (!response.ok) throw new Error(t('error.saveFailed'));

            // Секрет сохранён — очищаем поле, чтобы не хранить его в DOM
            inputClientSecret.value = '';

            Dashboard.processPluginConfigurationUpdateResult();
            console.log("[NextcloudOAuth2] Настройки сохранены");
        } catch (err) {
            console.error("[NextcloudOAuth2] Ошибка сохранения:", err);
            alert(t('error.saveUnexpected'));
        }
    }

    async function onCopySsoUrl() {
        const url = inputSsoLoginUrl.value;
        try {
            await navigator.clipboard.writeText(url);
            alert(t('status.copied'));
        } catch (err) {
            // Fallback для старых браузеров / HTTP-контекста
            inputSsoLoginUrl.select();
            document.execCommand('copy');
        }
    }

    async function onTestConnection() {
        setStatus('info', t('status.testing'));
        btnTestConnection.disabled = true;

        try {
            const response = await fetch(ApiClient.getUrl('NextcloudOAuth2/TestConnection'), {
                method: 'POST',
                headers: {
                    'Authorization': 'MediaBrowser Token=' + ApiClient.accessToken()
                }
            });

            const result = await response.json();

            if (result.Success) {
                setStatus('success', t('status.success', result.ServerVersion || t('status.unknown')));
            } else {
                setStatus('error', t('status.error', result.ErrorMessage || t('status.errorUnknown')));
            }
        } catch (err) {
            setStatus('error', t('status.connectionError', err.message));
        } finally {
            btnTestConnection.disabled = false;
        }
    }

    // Безопасный вывод текста: данные приходят от внешнего сервера (Nextcloud),
    // поэтому используем textContent вместо innerHTML
    function setStatus(kind, message) {
        connectionStatus.textContent = '';
        const p = document.createElement('p');
        p.textContent = (kind === 'success' ? '✓ ' : kind === 'error' ? '✗ ' : '') + message;
        if (kind === 'success') {
            p.style.color = 'green';
        } else if (kind === 'error') {
            p.style.color = 'red';
        }
        connectionStatus.appendChild(p);
    }
}
