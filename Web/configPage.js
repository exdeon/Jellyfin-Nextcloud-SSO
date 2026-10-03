export default function (view) {
    console.log("[NextcloudOAuth2] Страница настроек инициализирована");

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

            if (!response.ok) throw new Error('Не удалось загрузить настройки');

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

            if (!response.ok) throw new Error('Ошибка сохранения');

            // Секрет сохранён — очищаем поле, чтобы не хранить его в DOM
            inputClientSecret.value = '';

            Dashboard.processPluginConfigurationUpdateResult();
            console.log("[NextcloudOAuth2] Настройки сохранены");
        } catch (err) {
            console.error("[NextcloudOAuth2] Ошибка сохранения:", err);
            alert('Не удалось сохранить настройки. Проверьте консоль.');
        }
    }

    async function onCopySsoUrl() {
        const url = inputSsoLoginUrl.value;
        try {
            await navigator.clipboard.writeText(url);
            alert('Ссылка скопирована');
        } catch (err) {
            // Fallback для старых браузеров / HTTP-контекста
            inputSsoLoginUrl.select();
            document.execCommand('copy');
        }
    }

    async function onTestConnection() {
        setStatus('info', 'Проверка подключения...');
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
                setStatus('success', 'Подключение успешно! Nextcloud версия: ' + (result.ServerVersion || 'неизвестно'));
            } else {
                setStatus('error', 'Ошибка: ' + (result.ErrorMessage || 'неизвестная ошибка'));
            }
        } catch (err) {
            setStatus('error', 'Ошибка подключения: ' + err.message);
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
