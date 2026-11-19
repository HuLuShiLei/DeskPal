import { render } from "preact";
import { App } from "./App";
import { CardPage } from "./pages/CardPage";
import { LoginPage } from "./pages/LoginPage";
import { createBridge } from "./bridge";
import { setLocale } from "./i18n";
import { installWindowDrag, setPlatform } from "./platform";
import { applyTheme, watchSystemTheme } from "./theme";
import "./styles.css";

const params = new URLSearchParams(location.search);
setPlatform(params.get("platform"));
setLocale(params.get("lang"));
applyTheme(params.get("theme"));
watchSystemTheme();

const page = params.get("page");
void createBridge().then((bridge) => {
  installWindowDrag(bridge);
  render(
    page === "login" ? <LoginPage bridge={bridge} /> : page === "card" ? <CardPage bridge={bridge} /> : <App bridge={bridge} />,
    document.getElementById("app")!,
  );
});