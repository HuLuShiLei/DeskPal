import { render } from "preact";
import { App } from "./App";
import { CardPage } from "./pages/CardPage";
import { LoginPage } from "./pages/LoginPage";
import { createBridge } from "./bridge";
import { applyTheme, watchSystemTheme } from "./theme";
import "./styles.css";

applyTheme(new URLSearchParams(location.search).get("theme"));
watchSystemTheme();

const page = new URLSearchParams(location.search).get("page");
void createBridge().then((bridge) =>
  render(
    page === "login" ? <LoginPage bridge={bridge} /> : page === "card" ? <CardPage bridge={bridge} /> : <App bridge={bridge} />,
    document.getElementById("app")!,
  ),
);