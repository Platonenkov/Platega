// Opens the Platega payment page in a new tab while the shop stays open.
// Browsers block window.open outside a user gesture, and with Blazor Server the payment URL is known only
// after a server round trip. So a blank tab is opened synchronously on the click (capture phase, before
// Blazor dispatches the event) and receives the URL once the payment is created.
(function () {
    let pending = null;

    document.addEventListener('click', function (event) {
        const trigger = event.target instanceof Element ? event.target.closest('[data-payment-window]') : null;
        if (!trigger || trigger.disabled) {
            return;
        }

        try {
            pending = window.open('', '_blank');
        } catch {
            pending = null;
        }
    }, true);

    window.plategaPaymentWindow = {
        // Navigates the prepared tab to the payment page; returns false when no tab could be opened.
        open: function (url) {
            const tab = pending;
            pending = null;
            if (!tab || tab.closed) {
                return false;
            }

            tab.opener = null;
            tab.location.href = url;
            return true;
        },

        // Closes the prepared tab when the payment could not be created.
        cancel: function () {
            const tab = pending;
            pending = null;
            if (tab && !tab.closed) {
                tab.close();
            }
        }
    };
})();
