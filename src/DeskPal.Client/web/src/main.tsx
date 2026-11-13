import { render } from "preact";
import { App } from "./App";
import { CardPage } from "./pages/CardPage";
import { LoginPage } from "./pages/LoginPage";
import { createBridge } from "./bridge";
import { setLocale } from "./i18n";
import { applyTheme, watchSystemTheme } from "./theme";
import "./styles.css";

const params = new URLSearchParams(location.search);
setLocale(params.get("lang"));
applyTheme(params.get("theme"));
watchSystemTheme();

const page = params.get("page");
void createBridge().then((bridge) =>
  render(
    page === "login" ? <LoginPage bridge={bridge} /> : page === "card" ? <CardPage bridge={bridge} /> : <App bridge={bridge} />,
    document.getElementById("app")!,
  ),
);