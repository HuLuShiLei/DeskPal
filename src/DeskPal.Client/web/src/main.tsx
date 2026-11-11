import { render } from "preact";
import { App } from "./App";
import { LoginPage } from "./pages/LoginPage";
import { createBridge } from "./bridge";
import "./styles.css";

const login = new URLSearchParams(location.search).get("page") === "login";
void createBridge().then((bridge) =>
  render(login ? <LoginPage bridge={bridge} /> : <App bridge={bridge} />, document.getElementById("app")!),
);