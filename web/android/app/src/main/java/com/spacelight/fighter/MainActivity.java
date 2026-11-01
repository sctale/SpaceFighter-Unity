package com.spacelight.fighter;

import android.os.Build;
import android.os.Bundle;
import android.view.View;
import android.view.WindowInsets;
import android.view.WindowInsetsController;

import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        pinWebViewTextZoom();
        enterImmersiveMode();
    }

    /**
     * Android scales WebView text by the system font scale (settings.textZoom).
     * A game HUD must lay out identically on every device, so pin the zoom to
     * 100% — otherwise oversized system fonts stretch the menus and make
     * internally scrolled panels jump around when controls gain focus.
     */
    private void pinWebViewTextZoom() {
        if (this.bridge != null && this.bridge.getWebView() != null) {
            this.bridge.getWebView().getSettings().setTextZoom(100);
        }
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) {
            enterImmersiveMode();
        }
    }

    private void enterImmersiveMode() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            WindowInsetsController controller = getWindow().getInsetsController();
            if (controller != null) {
                // Hide only the status bar. Hiding the navigation bar as well
                // triggers Android's intrusive "Viewing full screen" dialog.
                controller.hide(WindowInsets.Type.statusBars());
            }
        } else {
            getWindow().getDecorView().setSystemUiVisibility(
                    View.SYSTEM_UI_FLAG_FULLSCREEN | View.SYSTEM_UI_FLAG_LAYOUT_STABLE);
        }
    }
}
